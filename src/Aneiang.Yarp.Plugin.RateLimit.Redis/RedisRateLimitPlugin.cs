using Aneiang.Yarp.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Plugin.RateLimit.Redis;

/// <summary>
/// Route-scoped distributed rate limiting backed by Redis. Counters are shared across all
/// gateway instances, which keeps limits precise in horizontally scaled deployments.
/// The Redis client (StackExchange.Redis) is an optional runtime dependency: when it is not
/// deployed next to the gateway the middleware fails open and only logs periodically.
/// </summary>
public class RedisRateLimitPlugin : IGatewayPlugin
{
    public string PluginId => "rate-limit-redis";
    public string DisplayName => "Distributed Rate Limit (Redis)";
    public string Version => "1.0";
    public PluginManifest Manifest => new(
        PluginId, DisplayName, Version,
        [PluginScope.Route],
        [PluginCapability.ProxyPipeline],
        210,
        new PluginResourceRequirements(RequestMiddleware: true),
        [new PluginSchemaReference(1, "builtin://plugins/rate-limit-redis/schemas/config-v1.json", """
            {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"properties":{"enabled":{"type":"boolean","default":true},"redisConnectionString":{"type":"string","minLength":1,"title":"Redis connection string","description":"e.g. localhost:6379 or user:pass@host:6379,ssl=True"},"algorithm":{"type":"string","enum":["FixedWindow","SlidingWindow","TokenBucket"],"default":"FixedWindow"},"limit":{"type":"integer","minimum":1,"default":100},"windowSeconds":{"type":"integer","minimum":1,"default":60},"keyPrefix":{"type":"string","minLength":1,"default":"aneiang:rl"},"burstBalance":{"type":"integer","minimum":0,"default":0,"description":"TokenBucket only: extra burst capacity above limit"}}}
            """)],
        "Route-scoped distributed rate limiting with Redis-backed atomic counters.");

    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null)
    {
        services.AddSingleton<IDistributedRateLimitStore, RedisLuaRateLimitStore>();
    }

    public void ConfigureMiddleware(IApplicationBuilder app) { }

    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline) =>
        proxyPipeline.UseMiddleware<RedisRateLimitMiddleware>();

    public void ConfigureDashboard(IPluginDashboardBuilder builder) { }
}
