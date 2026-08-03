using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Reads the enabled rate-limit binding from the current gateway snapshot and enforces per-partition limits.
/// </summary>
public sealed class RateLimitMiddleware : GatewayMiddlewareBase
{
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly INotificationService _notificationService;
    private readonly GatewayPluginExecutionPlanProvider _executionPlans;
    private readonly IRateLimiterStore _limiterStore;

    private const int MaxLimiterCount = 2000;
    private static readonly TimeSpan DefaultCleanupInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaleLimiterThreshold = TimeSpan.FromMinutes(5);
    private long _lastCleanupTicks = DateTime.Now.Ticks;

    public RateLimitMiddleware(
        RequestDelegate next,
        ILogger<RateLimitMiddleware> logger,
        IOptions<DashboardOptions> dashOptions,
        IGatewayPluginManager pluginManager,
        IRateLimiterStore limiterStore,
        GatewayPluginExecutionPlanProvider executionPlans,
        INotificationService? notificationService = null,
        IDynamicYarpConfigService? yarpConfig = null)
        : base(next, dashOptions, pluginManager, yarpConfig)
    {
        _logger = logger;
        _notificationService = notificationService ?? NullNotificationService.Instance;
        _executionPlans = executionPlans;
        _limiterStore = limiterStore;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsDashboardRequest(context))
        {
            await Next(context);
            return;
        }

        var routeId = context.Features.Get<IReverseProxyFeature>()?.Route?.Config?.RouteId;
        if (string.IsNullOrWhiteSpace(routeId) ||
            !TryGetBindingConfig(routeId, out var routeUid, out var config))
        {
            await Next(context);
            return;
        }

        var routeKey = routeId;
        var routeScopeId = routeUid;

        var partitionValue = GetPartitionValue(context, config.PartitionKey);

        var configFingerprint = $"{config.Algorithm}:{config.PermitLimit}:{config.Window}:{config.QueueLimit}:{config.SegmentsPerWindow}:{config.TokenLimit}:{config.TokensPerPeriod}:{config.ReplenishmentPeriod}";
        var limiterKey = string.IsNullOrEmpty(routeScopeId)
            ? $"global:{partitionValue}:{configFingerprint}"
            : $"{routeScopeId}:{partitionValue}:{configFingerprint}";

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

    private bool TryGetBindingConfig(
        string routeId,
        out string routeUid,
        out RouteRateLimitConfig config)
    {
        routeUid = string.Empty;
        config = null!;

        if (!_executionPlans.Current.RateLimitByRoute.TryGetValue(routeId, out var compiled) || !compiled.Enabled)
            return false;

        routeUid = compiled.RouteUid;
        config = new RouteRateLimitConfig
        {
            Enabled = true,
            Algorithm = compiled.Algorithm,
            PermitLimit = compiled.PermitLimit,
            Window = compiled.Window,
            QueueLimit = compiled.QueueLimit,
            PartitionKey = compiled.PartitionKey,
            SegmentsPerWindow = compiled.SegmentsPerWindow,
            TokenLimit = compiled.TokenLimit,
            TokensPerPeriod = compiled.TokensPerPeriod,
            ReplenishmentPeriod = compiled.ReplenishmentPeriod
        };
        return true;
    }

    private RateLimiter GetOrCreateLimiter(string key, RouteRateLimitConfig config)
    {
        TryCleanup();

        var entry = _limiterStore.GetOrAdd(key, () => CreateLimiter(key, config));
        entry.LastAccessedAt = DateTime.Now;
        return entry.Limiter;
    }

    private RateLimiter CreateLimiter(string key, RouteRateLimitConfig config)
    {
        var window = ParseTimeSpan(config.Window);

        var queueLimit = Math.Max(0, config.QueueLimit);
        return config.Algorithm switch
        {
            RateLimitAlgorithm.SlidingWindow => new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, config.PermitLimit),
                Window = window,
                SegmentsPerWindow = Math.Clamp(config.SegmentsPerWindow, 2, 100),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = queueLimit
            }),

            RateLimitAlgorithm.TokenBucket => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = Math.Max(1, config.TokenLimit),
                TokensPerPeriod = Math.Max(1, config.TokensPerPeriod),
                ReplenishmentPeriod = ParseTimeSpan(config.ReplenishmentPeriod),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = queueLimit,
                AutoReplenishment = true
            }),

            RateLimitAlgorithm.Concurrency => new ConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = Math.Max(1, config.PermitLimit),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = queueLimit
            }),

            _ => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, config.PermitLimit),
                Window = window,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = queueLimit
            })
        };
    }

    private void TryCleanup()
    {
        var lastTicks = Interlocked.Read(ref _lastCleanupTicks);
        if (DateTime.Now.Ticks - lastTicks < DefaultCleanupInterval.Ticks)
            return;

        Interlocked.Exchange(ref _lastCleanupTicks, DateTime.Now.Ticks);
        _limiterStore.Cleanup(StaleLimiterThreshold, MaxLimiterCount);
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
        public int SegmentsPerWindow { get; set; } = 4;
        public int TokenLimit { get; set; } = 100;
        public int TokensPerPeriod { get; set; } = 100;
        public string ReplenishmentPeriod { get; set; } = "1s";
    }
}
