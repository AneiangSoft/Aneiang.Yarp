using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Infrastructure;
using System.Threading.RateLimiting;

namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>
/// Configures ASP.NET Core rate limiter from Gateway:Dashboard config section.
/// Supports multiple algorithms (FixedWindow, SlidingWindow, TokenBucket, Concurrency)
/// and multiple partition keys (IpAddress, UserId, Route, Global).
/// Registered as a singleton so it runs once after DI is built.
/// </summary>
internal sealed class RateLimitConfigProvider : IConfigureOptions<RateLimiterOptions>
{
    private readonly IConfiguration _config;

    public RateLimitConfigProvider(IConfiguration config)
    {
        _config = config;
    }

    public void Configure(RateLimiterOptions options)
    {
        var section = _config.GetSection("Gateway:Dashboard:RateLimit");
        
        if (!section.Exists())
        {
            // Fallback: try old flat config keys
            var enable = bool.TryParse(_config["Gateway:Dashboard:EnableRateLimiting"], out var e) && e;
            if (!enable) return;
            
            var limit = int.TryParse(_config["Gateway:Dashboard:RateLimitPermitLimit"], out var pl) ? pl : 100;
            var window = _config["Gateway:Dashboard:RateLimitWindow"];
            var ql = int.TryParse(_config["Gateway:Dashboard:RateLimitQueueLimit"], out var q) ? q : 10;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetFixedWindowLimiter("gateway-fixed", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, limit),
                    Window = TimeSpan.TryParse(window, out var ts) ? ts : TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = ql
                }));
            options.RejectionStatusCode = 429;
            return;
        }

        var enabled = section["Enabled"];
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            return;

        var algorithm = Enum.TryParse<RateLimitAlgorithm>(section["Algorithm"], true, out var alg) 
            ? alg 
            : RateLimitAlgorithm.FixedWindow;
        var permitLimit = int.TryParse(section["PermitLimit"], out var pl2) ? pl2 : 100;
        var windowStr = section["Window"] ?? "1m";
        var queueLimit = int.TryParse(section["QueueLimit"], out var ql2) ? ql2 : 10;
        var tokenCapacity = int.TryParse(section["TokenBucketCapacity"], out var tc) ? tc : 100;
        var tokenRefillRate = int.TryParse(section["TokenBucketRefillRate"], out var trr) ? trr : 10;
        var concurrencyLimit = int.TryParse(section["ConcurrencyLimit"], out var cl) ? cl : 50;
        var partitionKey = section["PartitionKey"] ?? "IpAddress";

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var partition = GetPartition(context, partitionKey);
            
            return algorithm switch
            {
                RateLimitAlgorithm.FixedWindow => RateLimitPartition.GetFixedWindowLimiter(
                    partition,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, permitLimit),
                        Window = TimeSpan.TryParse(windowStr, out var ts) ? ts : TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = queueLimit
                    }),
                
                RateLimitAlgorithm.SlidingWindow => RateLimitPartition.GetSlidingWindowLimiter(
                    partition,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, permitLimit),
                        Window = TimeSpan.TryParse(windowStr, out var ts2) ? ts2 : TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 10,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = queueLimit
                    }),
                
                RateLimitAlgorithm.TokenBucket => RateLimitPartition.GetTokenBucketLimiter(
                    partition,
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = Math.Max(1, tokenCapacity),
                        TokensPerPeriod = Math.Max(0, tokenRefillRate),
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = queueLimit,
                        AutoReplenishment = true
                    }),
                
                RateLimitAlgorithm.Concurrency => RateLimitPartition.GetConcurrencyLimiter(
                    partition,
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = Math.Max(1, concurrencyLimit),
                        QueueLimit = queueLimit
                    }),
                
                _ => RateLimitPartition.GetFixedWindowLimiter(
                    partition,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, permitLimit),
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = queueLimit
                    })
            };
        });

        options.RejectionStatusCode = 429;
    }

    private static string GetPartition(HttpContext context, string partitionKey)
    {
        return partitionKey.ToLowerInvariant() switch
        {
            "ipaddress" => GetClientIp(context) ?? "unknown",
            "userid" => context.User?.Identity?.Name ?? "anonymous",
            "global" => "gateway-global",
            _ => GetClientIp(context) ?? "unknown"
        };
    }

    private static string? GetClientIp(HttpContext context)
    {
        // Check X-Forwarded-For header first
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var value = forwardedFor.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
            {
                var commaIdx = value.IndexOf(',');
                return commaIdx > 0 ? value.AsSpan(0, commaIdx).Trim().ToString() : value.Trim().ToString();
            }
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}
