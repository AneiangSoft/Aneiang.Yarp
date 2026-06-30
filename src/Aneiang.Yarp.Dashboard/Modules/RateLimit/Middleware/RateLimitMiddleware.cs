using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Modules.Notification.Services;
using Aneiang.Yarp.Services;
using System.Threading.RateLimiting;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Modules.RateLimit.Middleware;

/// <summary>
/// Route-level rate limiting middleware.
/// Reads RateLimit:* metadata from the matched route and enforces per-partition rate limits.
/// Falls back to global DashboardOptions.RateLimit when no route-level metadata is configured.
/// </summary>
public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly RateLimitOptions _globalOptions;
    private readonly IGatewayPluginManager _pluginManager;
    private readonly string _dashPrefix;
    private readonly INotificationService _notificationService;
    private readonly IDynamicYarpConfigService? _yarpConfig;

    private const string ContentRoot = "/_content/Aneiang.Yarp.Dashboard";

    private static readonly ConcurrentDictionary<string, RateLimiter> Limiters = new();

    private static readonly TimeSpan DefaultCleanupInterval = TimeSpan.FromMinutes(5);
    private static DateTime _lastCleanup = DateTime.UtcNow;

    public RateLimitMiddleware(
        RequestDelegate next,
        ILogger<RateLimitMiddleware> logger,
        IOptions<RateLimitOptions> options,
        IOptions<DashboardOptions> dashOptions,
        IGatewayPluginManager pluginManager,
        INotificationService? notificationService = null,
        IDynamicYarpConfigService? yarpConfig = null)
    {
        _next = next;
        _logger = logger;
        _globalOptions = options.Value;
        _pluginManager = pluginManager;
        _dashPrefix = "/" + dashOptions.Value.RoutePrefix.Trim('/');
        _notificationService = notificationService ?? NullNotificationService.Instance;
        _yarpConfig = yarpConfig;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_pluginManager.IsPluginEnabled("rate-limit"))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";

        if (path.StartsWith(_dashPrefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(ContentRoot, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var routeMeta = proxyFeature?.Route?.Config?.Metadata;
        var routeKey = proxyFeature?.Route?.Config?.RouteId;

        var config = ResolveConfig(routeMeta, out var routeId);
        var routeScopeId = ResolveRouteScopeId(routeKey ?? routeId);

        if (!config.Enabled)
        {
            await _next(context);
            return;
        }

        var partitionValue = GetPartitionValue(context, config.PartitionKey);

        var limiterKey = string.IsNullOrEmpty(routeScopeId)
            ? $"global:{partitionValue}"
            : $"{routeScopeId}:{partitionValue}";

        var limiter = GetOrCreateLimiter(limiterKey, config);

        using var lease = await limiter.AcquireAsync(1, context.RequestAborted);

        if (!lease.IsAcquired)
        {
            var clientIp = GetClientIp(context) ?? "unknown";
            _logger.LogWarning(
                "Rate limit exceeded for {LimiterKey} (algorithm={Algorithm}, limit={PermitLimit}, window={Window})",
                limiterKey, config.Algorithm, config.PermitLimit, config.Window);

            _notificationService.NotifyRateLimitExceeded(clientIp, routeKey ?? routeId);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json";

            var retryAfter = GetRetryAfterSeconds(config);
            if (retryAfter > 0)
                context.Response.Headers["Retry-After"] = retryAfter.ToString();

            await context.Response.WriteAsJsonAsync(new
            {
                error = "Too Many Requests",
                message = $"Rate limit exceeded. Try again in {retryAfter}s.",
                retryAfter
            });
            return;
        }

        await _next(context);
    }

    private RouteRateLimitConfig ResolveConfig(IReadOnlyDictionary<string, string>? routeMeta, out string? routeId)
    {
        routeId = null;

        if (routeMeta == null)
            return FromGlobalOptions();

        if (routeMeta.TryGetValue("Policy:Id", out var policyId))
            routeId = policyId;
        if (routeMeta.TryGetValue("RouteId", out var rid))
            routeId = rid;

        if (!routeMeta.TryGetValue("RateLimit:Enabled", out var enabledStr) ||
            !bool.TryParse(enabledStr, out var routeEnabled))
        {
            return FromGlobalOptions();
        }

        if (!routeEnabled)
        {
            return new RouteRateLimitConfig { Enabled = false };
        }

        var algorithm = routeMeta.TryGetValue("RateLimit:Algorithm", out var algStr)
            ? ParseAlgorithm(algStr)
            : _globalOptions.Algorithm;

        var permitLimit = routeMeta.TryGetValue("RateLimit:PermitLimit", out var plStr) &&
                          int.TryParse(plStr, out var pl)
            ? pl
            : _globalOptions.PermitLimit;

        var window = routeMeta.TryGetValue("RateLimit:Window", out var winStr)
            ? winStr
            : _globalOptions.Window;

        var queueLimit = routeMeta.TryGetValue("RateLimit:QueueLimit", out var qlStr) &&
                         int.TryParse(qlStr, out var ql)
            ? ql
            : _globalOptions.QueueLimit;

        var partitionKey = routeMeta.TryGetValue("RateLimit:PartitionKey", out var pkStr) &&
                           !string.IsNullOrWhiteSpace(pkStr)
            ? pkStr
            : _globalOptions.PartitionKey;

        return new RouteRateLimitConfig
        {
            Enabled = true,
            Algorithm = algorithm,
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = queueLimit,
            PartitionKey = partitionKey
        };
    }

    private RouteRateLimitConfig FromGlobalOptions()
    {
        return new RouteRateLimitConfig
        {
            Enabled = _globalOptions.Enabled,
            Algorithm = _globalOptions.Algorithm,
            PermitLimit = _globalOptions.PermitLimit,
            Window = _globalOptions.Window,
            QueueLimit = _globalOptions.QueueLimit,
            PartitionKey = _globalOptions.PartitionKey
        };
    }

    private RateLimiter GetOrCreateLimiter(string key, RouteRateLimitConfig config)
    {
        TryCleanup();

        return Limiters.GetOrAdd(key, _ => CreateLimiter(key, config));
    }

    private RateLimiter CreateLimiter(string key, RouteRateLimitConfig config)
    {
        var window = ParseTimeSpan(config.Window);

        // QueueLimit is forced to 0: rate-limited requests must be rejected immediately
        // (HTTP 429) rather than queued, which would cause the client to hang indefinitely.
        return config.Algorithm switch
        {
            RateLimitAlgorithm.SlidingWindow => new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, config.PermitLimit),
                Window = window,
                SegmentsPerWindow = Math.Max(2, Math.Min(20, config.PermitLimit / 2)),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }),

            RateLimitAlgorithm.TokenBucket => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = Math.Max(1, config.PermitLimit),
                TokensPerPeriod = Math.Max(1, config.PermitLimit / Math.Max(1, (int)window.TotalSeconds)),
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }),

            RateLimitAlgorithm.Concurrency => new ConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = Math.Max(1, config.PermitLimit),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }),

            _ => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, config.PermitLimit),
                Window = window,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            })
        };
    }

    private void TryCleanup()
    {
        if (DateTime.UtcNow - _lastCleanup < DefaultCleanupInterval)
            return;

        _lastCleanup = DateTime.UtcNow;

        if (Limiters.Count <= 10000)
            return;

        var keysToRemove = Limiters.Keys.Take(Limiters.Count / 2).ToList();
        foreach (var k in keysToRemove)
        {
            if (Limiters.TryRemove(k, out var limiter))
                limiter.Dispose();
        }
    }

    private string? ResolveRouteScopeId(string? routeKey)
    {
        if (string.IsNullOrWhiteSpace(routeKey)) return null;

        var routeUid = _yarpConfig?.GetDynamicConfig()?.Routes.FirstOrDefault(r =>
            string.Equals(r.Config.RouteId, routeKey, StringComparison.OrdinalIgnoreCase))?.RouteUid;

        return string.IsNullOrWhiteSpace(routeUid) ? StableUidFromKey("route", routeKey) : routeUid;
    }

    private static string StableUidFromKey(string prefix, string key)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(prefix + ":" + key));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }

    private static string GetPartitionValue(HttpContext context, string partitionKey)
    {
        return partitionKey.ToLowerInvariant() switch
        {
            "ipaddress" => GetClientIp(context) ?? "unknown",
            "userid" => context.User?.Identity?.Name ?? "anonymous",
            "route" => context.Request.Path.Value ?? "/",
            "global" => "gateway-global",
            _ => GetClientIp(context) ?? "unknown"
        };
    }

    private static string? GetClientIp(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var value = forwardedFor.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
            {
                var commaIdx = value.IndexOf(',');
                return commaIdx > 0
                    ? value.AsSpan(0, commaIdx).Trim().ToString()
                    : value.Trim().ToString();
            }
        }

        if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp))
        {
            var value = realIp.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                return value.Trim().ToString();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }

    private static TimeSpan ParseTimeSpan(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return TimeSpan.FromMinutes(1);

        if (TimeSpan.TryParse(value, out var ts))
            return ts;

        var span = value.AsSpan().Trim();
        if (span.Length < 2)
            return TimeSpan.FromMinutes(1);

        var numPart = span[..^1];
        var unit = char.ToLowerInvariant(span[^1]);

        if (!double.TryParse(numPart, out var num))
            return TimeSpan.FromMinutes(1);

        return unit switch
        {
            's' => TimeSpan.FromSeconds(num),
            'm' => TimeSpan.FromMinutes(num),
            'h' => TimeSpan.FromHours(num),
            'd' => TimeSpan.FromDays(num),
            _ => TimeSpan.FromMinutes(num)
        };
    }

    private static RateLimitAlgorithm ParseAlgorithm(string value)
    {
        return Enum.TryParse<RateLimitAlgorithm>(value, true, out var alg)
            ? alg
            : RateLimitAlgorithm.FixedWindow;
    }

    private static int GetRetryAfterSeconds(RouteRateLimitConfig config)
    {
        var window = ParseTimeSpan(config.Window);
        return Math.Max(1, (int)Math.Ceiling(window.TotalSeconds));
    }

    private sealed class RouteRateLimitConfig
    {
        public bool Enabled { get; set; }
        public RateLimitAlgorithm Algorithm { get; set; } = RateLimitAlgorithm.FixedWindow;
        public int PermitLimit { get; set; } = 100;
        public string Window { get; set; } = "1m";
        public int QueueLimit { get; set; } = 0;
        public string PartitionKey { get; set; } = "IpAddress";
    }
}
