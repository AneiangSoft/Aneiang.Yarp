using Aneiang.Yarp.Dashboard.Infrastructure;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Alert;

/// <summary>
/// Abstraction for emitting gateway alert events (WAF blocks, circuit breaker trips, rate limits, config changes, ...).
/// Allows components to fire alerts without depending on a specific sink (webhook, log, dashboard).
/// </summary>
public interface IGatewayAlertService
{
    /// <summary>Send an alert when a circuit breaker opens.</summary>
    void AlertCircuitBreakerOpen(string clusterId, string? destinationId = null);

    /// <summary>Send an alert when all retry attempts are exhausted.</summary>
    void AlertRetryExhausted(string clusterId, string routeId, int attempts, int statusCode);

    /// <summary>Send an alert when WAF blocks a request.</summary>
    void AlertWafBlock(string clientIp, string blockReason, string? uri = null);

    /// <summary>Send an alert when a proxy error occurs.</summary>
    void AlertProxyError(string clusterId, string? destinationId, string errorMessage, string? stackTrace = null);

    /// <summary>Send an alert when rate limit is exceeded.</summary>
    void AlertRateLimitExceeded(string clientIp, string? routeId = null);

    /// <summary>Send a custom alert with arbitrary type, title and message.</summary>
    void AlertCustom(string alertType, string title, string message, string severity = "Info");
}
