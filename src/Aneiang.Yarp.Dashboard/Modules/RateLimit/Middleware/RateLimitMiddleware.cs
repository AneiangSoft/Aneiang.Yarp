using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Middleware;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Dashboard.Modules.Notification.Services;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Infrastructure;
using System.Threading.RateLimiting;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Modules.RateLimit.Middleware;

/// <summary>
/// Route-level rate limiting middleware.
/// Reads RateLimit:* metadata from the matched route and enforces per-partition rate limits.
/// Falls back to global DashboardOptions.RateLimit when no route-level metadata is configured.
/// </summary>
public sealed class RateLimitMiddleware : GatewayMiddlewareBase
{
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly RateLimitOptions _globalOptions;
    private readonly INotificationService _notificationService;
    private readonly IDynamicYarpConfigService? _yarpConfig;
    private readonly IRateLimiterStore _limiterStore;

    private const int MaxLimiterCount = 2000;
    private static readonly TimeSpan DefaultCleanupInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaleLimiterThreshold = TimeSpan.FromMinutes(5);
    private DateTime _lastCleanup = DateTime.UtcNow;

    public RateLimitMiddleware(
        RequestDelegate next,
        ILogger<RateLimitMiddleware> logger,
        IOptions<RateLimitOptions> options,
        IOptions<DashboardOptions> dashOptions,
        IGatewayPluginManager pluginManager,
        IRateLimiterStore limiterStore,
        INotificationService? notificationService = null,
        IDynamicYarpConfigService? yarpConfig = null)
        : base(next, dashOptions, pluginManager, yarpConfig)
    {
        _logger = logger;
        _globalOptions = options.Value;
        _notificationService = notificationService ?? NullNotificationService.Instance;
        _yarpConfig = yarpConfig;
        _limiterStore = limiterStore;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsPluginEnabled("rate-limit"))
        {
            await Next(context);
            return;
        }

        if (IsDashboardRequest(context))
        {
            await Next(context);
            return;
        }

        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var routeMeta = proxyFeature?.Route?.Config?.Metadata;
        var routeKey = proxyFeature?.Route?.Config?.RouteId;

        var config = ResolveConfig(routeMeta, out var routeId);
        var routeScopeId = ResolveRouteScopeId(routeKey ?? routeId);

        if (!config.Enabled)
        {
            await Next(context);
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

        await Next(context);
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

        var entry = _limiterStore.GetOrAdd(key, () => CreateLimiter(key, config));
        entry.LastAccessedAt = DateTime.UtcNow;
        return entry.Limiter;
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
        _limiterStore.Cleanup(StaleLimiterThreshold, MaxLimiterCount);
    }

    private string? ResolveRouteScopeId(string? routeKey)
    {
        if (string.IsNullOrWhiteSpace(routeKey)) return null;

        var routeUid = _yarpConfig?.GetDynamicConfig()?.Routes.FirstOrDefault(r =>
            string.Equals(r.Config.RouteId, routeKey, StringComparison.OrdinalIgnoreCase))?.RouteUid;

        return string.IsNullOrWhiteSpace(routeUid) ? StableUid.FromKey("route", routeKey) : routeUid;
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
        return ClientIpResolver.GetClientIp(context);
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
