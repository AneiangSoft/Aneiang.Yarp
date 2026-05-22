using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace Aneiang.Yarp.Extensions;

/// <summary>
/// Configures ASP.NET Core rate limiter from Gateway:Dashboard config section.
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
        var enable = bool.TryParse(_config["Gateway:Dashboard:EnableRateLimiting"], out var e) && e;
        if (!enable) return;

        var permitLimit = int.TryParse(_config["Gateway:Dashboard:RateLimitPermitLimit"], out var pl) ? pl : 100;
        var window = _config["Gateway:Dashboard:RateLimitWindow"];
        var queueLimit = int.TryParse(_config["Gateway:Dashboard:RateLimitQueueLimit"], out var ql) ? ql : 10;

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
            RateLimitPartition.GetFixedWindowLimiter("gateway-fixed", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, permitLimit),
                Window = TimeSpan.TryParse(window, out var ts) ? ts : TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = queueLimit
            }));
        options.RejectionStatusCode = 429;
    }
}
