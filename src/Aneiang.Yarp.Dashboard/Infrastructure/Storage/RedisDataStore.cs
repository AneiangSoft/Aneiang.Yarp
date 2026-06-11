using System.Text.Json;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Storage;

/// <summary>
/// Redis-based <see cref="IDataStore"/> implementation.
/// Documents are stored as Redis Strings with key <c>{prefix}doc:{category}</c>.
/// Collections are stored as Redis Lists with key <c>{prefix}col:{category}</c>.
/// Uses a cached <see cref="IDatabase"/> and batch operations for reduced latency.
/// </summary>
public class RedisDataStore : IDataStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly RedisStorageOptions _options;
    private readonly ILogger<RedisDataStore> _logger;
    private ConnectionMultiplexer? _redis;
    private IDatabase? _db;
    private bool _initialized;

    public RedisDataStore(StorageOptions options, ILogger<RedisDataStore> logger)
    {
        _options = options.Redis;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        try
        {
            _redis = await ConnectionMultiplexer.ConnectAsync(_options.ConnectionString);
            _db = _redis.GetDatabase(_options.Database);
            _initialized = true;
            _logger.LogInformation("RedisDataStore initialized, database={Db}, prefix={Prefix}", _options.Database, _options.Prefix);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Redis: {Conn}", _options.ConnectionString);
            throw;
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (!_initialized) await InitializeAsync(ct);
    }

    /// <summary>
    /// Returns the cached database instance. The <c>ConnectionMultiplexer</c> already
    /// multiplexes connections internally; reusing <c>IDatabase</c> avoids the
    /// small overhead of repeated <c>GetDatabase()</c> calls.
    /// </summary>
    private IDatabase Db => _db ?? throw new InvalidOperationException("Redis not initialized");

    private string DocKey(string category) => $"{_options.Prefix}doc:{category}";
    private string ColKey(string category) => $"{_options.Prefix}col:{category}";

    // ── Document operations ──

    public async Task<T?> GetDocumentAsync<T>(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var value = await Db.StringGetAsync(DocKey(category));
        if (!value.HasValue)
            return default;

        return JsonSerializer.Deserialize<T>(value!, _jsonOptions);
    }

    public async Task SetDocumentAsync<T>(string category, T document, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var json = JsonSerializer.Serialize(document, _jsonOptions);
        await Db.StringSetAsync(DocKey(category), json);
        _logger.LogDebug("Document {Category} saved to Redis", category);
    }

    public async Task DeleteDocumentAsync(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await Db.KeyDeleteAsync(DocKey(category));
        _logger.LogInformation("Document {Category} deleted from Redis", category);
    }

    public async Task<bool> DocumentExistsAsync(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return await Db.KeyExistsAsync(DocKey(category));
    }

    // ── Collection operations ──

    /// <summary>
    /// Gets all items in a collection using a single pipelined batch.
    /// Sends LLEN + LRANGE in one RTT (instead of two sequential awaits).
    /// </summary>
    public async Task<IReadOnlyList<T>> GetCollectionAsync<T>(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var key = ColKey(category);

        // Batch LLEN + LRANGE into a single network round-trip
        var batch = Db.CreateBatch();
        var lenTask = batch.ListLengthAsync(key);
        var rangeTask = batch.ListRangeAsync(key, 0, -1);
        batch.Execute();

        var length = (int)await lenTask;
        if (length == 0)
            return Array.Empty<T>();

        var values = (RedisValue[])await rangeTask;
        var items = new List<T>(values.Length);
        foreach (var value in values)
        {
            if (value.HasValue)
            {
                var item = JsonSerializer.Deserialize<T>(value!, _jsonOptions);
                if (item is not null)
                    items.Add(item);
            }
        }

        return items.AsReadOnly();
    }

    public async Task AddToCollectionAsync<T>(string category, T item, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var json = JsonSerializer.Serialize(item, _jsonOptions);
        await Db.ListRightPushAsync(ColKey(category), json);
        _logger.LogDebug("Item added to collection {Category} in Redis", category);
    }

    public async Task ClearCollectionAsync(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await Db.KeyDeleteAsync(ColKey(category));
        _logger.LogInformation("Collection {Category} cleared from Redis", category);
    }

    public async Task<int> GetCollectionCountAsync(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var length = await Db.ListLengthAsync(ColKey(category));
        return (int)length;
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_redis is not null)
            await _redis.DisposeAsync();
    }
}
