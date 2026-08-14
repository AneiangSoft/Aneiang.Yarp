using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Infrastructure;
using Aneiang.Yarp.Infrastructure.Middleware;
using Aneiang.Yarp.Infrastructure.State;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Plugins;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Plugin.RateLimit.Redis;

/// <summary>
/// Route-level distributed rate limiting middleware. Reads the enabled rate-limit-redis binding
/// from the current gateway snapshot, consults the shared <see cref="IDistributedRateLimitStore"/>
/// and rejects overflowing requests with 429 + Retry-After. Standard rate-limit response headers
/// (X-RateLimit-Limit / X-RateLimit-Remaining) are always attached. When Redis is unreachable the
/// store fails open so the gateway stays available.
/// </summary>
public sealed class RedisRateLimitMiddleware : GatewayMiddlewareBase
{
    private readonly ILogger<RedisRateLimitMiddleware> _logger;
    private readonly GatewayPluginExecutionPlanProvider _executionPlans;
    private readonly IDistributedRateLimitStore _store;

    public RedisRateLimitMiddleware(
        RequestDelegate next,
        ILogger<RedisRateLimitMiddleware> logger,
        IOptions<GatewayMiddlewareOptions> dashOptions,
        IGatewayPluginManager pluginManager,
        IDistributedRateLimitStore store,
        GatewayPluginExecutionPlanProvider executionPlans,
        IDynamicYarpConfigService? yarpConfig = null)
        : base(next, dashOptions, pluginManager, yarpConfig)
    {
        _logger = logger;
        _store = store;
        _executionPlans = executionPlans;
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
            !_executionPlans.Current.RedisRateLimitByRoute.TryGetValue(routeId, out var config) ||
            !config.Enabled)
        {
            await Next(context);
            return;
        }

        var clientIp = ClientIpResolver.GetClientIp(context) ?? "unknown";
        var prefix = string.IsNullOrWhiteSpace(config.KeyPrefix) ? "aneiang:rl" : config.KeyPrefix;
        var key = $"{prefix}:{routeId}:{clientIp}";

        var result = await _store.TryAcquireAsync(
            config.Algorithm,
            key,
            config.Limit,
            config.WindowSeconds,
            config.BurstBalance,
            config.RedisConnectionString,
            context.RequestAborted);

        context.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();

        if (!result.Allowed)
        {
            _logger.LogWarning(
                "Distributed rate limit exceeded for {RateLimitKey} (algorithm={Algorithm}, limit={Limit}, window={WindowSeconds}s, retryAfter={RetryAfter}s)",
                key, config.Algorithm, config.Limit, config.WindowSeconds, result.RetryAfterSeconds);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Retry-After"] = result.RetryAfterSeconds.ToString();

            await context.Response.WriteAsJsonAsync(new
            {
                error = "Too Many Requests",
                message = $"Distributed rate limit exceeded. Try again in {result.RetryAfterSeconds}s.",
                retryAfter = result.RetryAfterSeconds
            });
            return;
        }

        await Next(context);
    }
}
