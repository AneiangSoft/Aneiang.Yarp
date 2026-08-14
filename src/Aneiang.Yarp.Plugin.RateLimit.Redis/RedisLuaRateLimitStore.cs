using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Plugin.RateLimit.Redis;

/// <summary>
/// Redis-backed <see cref="IDistributedRateLimitStore"/> that executes atomic Lua scripts for
/// FixedWindow (INCR+EXPIRE), SlidingWindow (sorted-set) and TokenBucket (hash) algorithms.
/// StackExchange.Redis is an optional dependency: it is located via reflection at first use.
/// When the client assembly or the Redis server is unavailable the store fails open so the
/// gateway keeps proxying traffic.
/// </summary>
public sealed class RedisLuaRateLimitStore : IDistributedRateLimitStore
{
    private const int UnavailableRetrySeconds = 30;

    private readonly ConcurrentDictionary<string, OptionalRedisConnection> _connections = new(StringComparer.Ordinal);
    private readonly ILogger<RedisLuaRateLimitStore> _logger;
    private long _lastOpenLogTick;

    public RedisLuaRateLimitStore(ILogger<RedisLuaRateLimitStore> logger) => _logger = logger;

    public async ValueTask<DistributedRateLimitResult> TryAcquireAsync(
        string algorithm,
        string key,
        long limit,
        int windowSeconds,
        int burstBalance,
        string? redisConnectionString,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0 || windowSeconds <= 0) return FailOpen(limit);

        var connection = _connections.GetOrAdd(redisConnectionString ?? string.Empty, cs => new OptionalRedisConnection(cs));
        var algorithmUpper = (algorithm ?? "FixedWindow").Trim().ToUpperInvariant();

        try
        {
            long[] parts = algorithmUpper switch
            {
                "SLIDINGWINDOW" => [1, limit, windowSeconds * 1000L],
                "TOKENBUCKET" => [1, limit, windowSeconds, burstBalance],
                _ => [1, limit, windowSeconds],
            };

            var reply = await connection.EvaluateAsync(
                GetScript(algorithmUpper),
                [key],
                parts,
                cancellationToken).ConfigureAwait(false);

            if (reply is not { Length: >= 3 })
                return FailOpen(limit);

            // Lua returns { allowed, remaining, retryAfterSeconds }.
            var allowed = ParseInteger(reply[0]) == 1;
            var remaining = Math.Max(0, ParseInteger(reply[1]));
            var retryAfter = Math.Max(0, ParseInteger(reply[2]));
            var capacity = algorithmUpper == "TOKENBUCKET" ? limit + Math.Max(0, burstBalance) : limit;
            return new DistributedRateLimitResult(allowed, capacity, allowed ? remaining : 0, allowed ? 0 : Math.Max(retryAfter, 1));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogOpen(exception, key);
            return FailOpen(algorithmUpper == "TOKENBUCKET" ? limit + Math.Max(0, burstBalance) : limit);
        }
    }

    private DistributedRateLimitResult FailOpen(long limit)
    {
        // Redis unavailable: allow the request rather than blocking the gateway.
        return new DistributedRateLimitResult(Allowed: true, Limit: limit, Remaining: limit, RetryAfterSeconds: 0);
    }

    private void LogOpen(Exception exception, string key)
    {
        // Rate-limit the warning itself to one entry per 30s.
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastOpenLogTick);
        if (now - last < UnavailableRetrySeconds * 1000) return;
        if (Interlocked.CompareExchange(ref _lastOpenLogTick, now, last) != last) return;
        _logger.LogWarning(exception, "Redis rate-limit store unavailable; failing open for key {RateLimitKey}", key);
    }

    private static long ParseInteger(string? value) =>
        long.TryParse(value, out var parsed) ? parsed : 0;

    private string GetScript(string algorithmUpper) => algorithmUpper switch
    {
        "SLIDINGWINDOW" => SlidingWindowScript,
        "TOKENBUCKET" => TokenBucketScript,
        _ => FixedWindowScript,
    };

    // ARGV: [1]=limit, [2]=windowSeconds. Returns { allowed, remaining, retryAfterSeconds }.
    private const string FixedWindowScript = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[2])
        end
        local limit = tonumber(ARGV[1])
        local ttl = redis.call('TTL', KEYS[1])
        if ttl < 1 then ttl = tonumber(ARGV[2]) end
        if current <= limit then
            return {1, limit - current, 0}
        end
        return {0, 0, ttl}
        """;

    // ARGV: [1]=limit, [2]=windowMilliseconds. Returns { allowed, remaining, retryAfterSeconds }.
    private const string SlidingWindowScript = """
        local time = redis.call('TIME')
        local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)
        local window = tonumber(ARGV[2])
        local limit = tonumber(ARGV[1])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], 0, now - window)
        local count = redis.call('ZCARD', KEYS[1])
        if count < limit then
            redis.call('ZADD', KEYS[1], now, now .. '-' .. math.random())
            redis.call('PEXPIRE', KEYS[1], window * 2)
            return {1, limit - count - 1, 0}
        end
        local oldest = redis.call('ZRANGE', KEYS[1], 0, 0, 'WITHSCORES')
        local retry = window
        if oldest[2] then
            retry = math.max(1, now - tonumber(oldest[2]) + window)
        end
        return {0, 0, math.ceil(retry / 1000)}
        """;

    // ARGV: [1]=limit, [2]=windowSeconds, [3]=burstBalance. Returns { allowed, remaining, retryAfterSeconds }.
    private const string TokenBucketScript = """
        local time = redis.call('TIME')
        local now = tonumber(time[1]) + tonumber(time[2]) / 1000000
        local capacity = tonumber(ARGV[1]) + tonumber(ARGV[3])
        local rate = tonumber(ARGV[1]) / math.max(1, tonumber(ARGV[2]))
        local state = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
        local tokens = tonumber(state[1])
        local stamp = tonumber(state[2])
        if tokens == nil then tokens = capacity end
        if stamp == nil then stamp = now end
        tokens = math.min(capacity, tokens + math.max(0, now - stamp) * rate)
        local allowed = 0
        if tokens >= 1 then
            tokens = tokens - 1
            allowed = 1
        end
        redis.call('HSET', KEYS[1], 'tokens', tokens, 'ts', now)
        redis.call('EXPIRE', KEYS[1], math.max(1, math.ceil(capacity / rate)) * 2 + 1)
        local retry = 0
        if allowed == 0 and rate > 0 then
            retry = math.max(1, math.ceil((1 - tokens) / rate))
        end
        return {allowed, math.floor(tokens), retry}
        """;

    /// <summary>
    /// Lazily connects to Redis through a reflection-based binding to StackExchange.Redis.
    /// The assembly is optional: when it is absent, or the server cannot be reached, evaluation
    /// returns null and callers fail open. Failed attempts enter a cooldown so the hot path
    /// does not hammer a downed server with connect attempts.
    /// </summary>
    private sealed class OptionalRedisConnection
    {
        private readonly string _connectionString;
        private readonly object _sync = new();
        private Assembly? _assembly;
        private object? _multiplexer;
        private object? _database;
        private MethodInfo? _scriptEvaluate;
        private ConstructorInfo? _keyCtor;
        private ConstructorInfo? _valueCtor;
        private Type? _keyType;
        private Type? _valueType;
        private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;

        public OptionalRedisConnection(string connectionString) => _connectionString = connectionString;

        public async Task<string[]?> EvaluateAsync(string script, string[] keys, long[] arguments, CancellationToken cancellationToken)
        {
            var database = GetDatabase();
            if (database == null) return null;

            var evaluate = _scriptEvaluate!;
            var parameters = evaluate.GetParameters();
            var redisKeys = CreateTypedArray(_keyType!, _keyCtor!, keys);
            var redisValues = CreateTypedArray(_valueType!, _valueCtor!, arguments.Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray());

            var invokeArgs = new object?[parameters.Length];
            invokeArgs[0] = script;
            invokeArgs[1] = redisKeys;
            invokeArgs[2] = redisValues;
            for (var index = 3; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                invokeArgs[index] = parameter.HasDefaultValue
                    ? parameter.DefaultValue
                    : (parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null);
            }

            var task = (Task)evaluate.Invoke(database, invokeArgs)!;
            await WaitWithCancellationAsync(task, cancellationToken).ConfigureAwait(false);
            var resultProperty = task.GetType().GetProperty("Result")?.GetValue(task);
            return ExtractParts(resultProperty);
        }

        private object? GetDatabase()
        {
            lock (_sync)
            {
                if (_database != null) return _database;
                if (DateTimeOffset.UtcNow < _nextAttempt) return null;
                _nextAttempt = DateTimeOffset.UtcNow.AddSeconds(UnavailableRetrySeconds);
            }

            try
            {
                var assembly = _assembly ??= LocateAssembly();
                if (assembly == null) return null;

                var multiplexerType = assembly.GetType("StackExchange.Redis.ConnectionMultiplexer", throwOnError: false);
                if (multiplexerType == null) return null;

                var connect = multiplexerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method => method.Name == "Connect" && method.GetParameters() is { Length: 1 } parameters && parameters[0].ParameterType == typeof(string));
                if (connect == null) return null;

                var connectionString = string.IsNullOrWhiteSpace(_connectionString)
                    ? "localhost:6379"
                    : _connectionString;
                var multiplexer = connect.Invoke(null, new object?[] { connectionString });
                if (multiplexer == null) return null;

                var getDatabase = multiplexerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method => method.Name == "GetDatabase" && method.GetParameters().Length == 0);
                object? database;
                if (getDatabase != null)
                {
                    database = getDatabase.Invoke(multiplexer, null);
                }
                else
                {
                    var fallback = multiplexerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(method => method.Name == "GetDatabase");
                    if (fallback == null) return null;
                    var callArguments = new object?[fallback.GetParameters().Length];
                    for (var index = 0; index < callArguments.Length; index++)
                    {
                        var parameter = fallback.GetParameters()[index];
                        callArguments[index] = parameter.ParameterType == typeof(int) ? -1 : null;
                    }
                    database = fallback.Invoke(multiplexer, callArguments);
                }
                if (database == null) return null;

                var evaluate = database.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(method => method.Name == "ScriptEvaluateAsync")
                    .Select(method => (method, parameters: method.GetParameters()))
                    .Where(candidate => candidate.parameters.Length >= 3 && candidate.parameters[0].ParameterType == typeof(string))
                    .OrderBy(candidate => candidate.parameters.Length)
                    .Select(candidate => candidate.method)
                    .FirstOrDefault();
                if (evaluate == null) return null;

                _keyType = assembly.GetType("StackExchange.Redis.RedisKey", throwOnError: false);
                _valueType = assembly.GetType("StackExchange.Redis.RedisValue", throwOnError: false);
                if (_keyType == null || _valueType == null) return null;
                _keyCtor = _keyType.GetConstructor([typeof(string)]);
                _valueCtor = _valueType.GetConstructor([typeof(string)]);
                if (_keyCtor == null || _valueCtor == null) return null;

                lock (_sync)
                {
                    _multiplexer = multiplexer;
                    _database = database;
                    _scriptEvaluate = evaluate;
                }
                return database;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Assembly? LocateAssembly()
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "StackExchange.Redis", StringComparison.OrdinalIgnoreCase));
            if (loaded != null) return loaded;

            var candidate = Path.Combine(AppContext.BaseDirectory, "StackExchange.Redis.dll");
            return File.Exists(candidate)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate)
                : null;
        }

        private static Array CreateTypedArray(Type elementType, ConstructorInfo constructor, string[] values)
        {
            var array = Array.CreateInstance(elementType, values.Length);
            for (var index = 0; index < values.Length; index++)
                array.SetValue(constructor.Invoke(new object?[] { values[index] }), index);
            return array;
        }

        private static string[] ExtractParts(object? redisResult)
        {
            if (redisResult is Array array)
            {
                var parts = new string[array.Length];
                for (var index = 0; index < array.Length; index++)
                    parts[index] = array.GetValue(index)?.ToString() ?? "0";
                return parts;
            }
            return [redisResult?.ToString() ?? "0"];
        }

        private static async Task WaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
            {
                var finished = await Task.WhenAny(task, completion.Task).ConfigureAwait(false);
                if (finished == completion.Task) throw new OperationCanceledException(cancellationToken);
                await task.ConfigureAwait(false);
            }
        }
    }
}
