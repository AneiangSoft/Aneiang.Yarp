using Aneiang.Yarp.Dashboard.Infrastructure.Alert;
using Aneiang.Yarp.Dashboard.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Notifications;

/// <summary>
/// Implementation of <see cref="IGatewayAlertService"/> that forwards gateway alerts
/// (circuit breaker trips, WAF blocks, rate limits, proxy errors) into the
/// <see cref="NotificationDispatcher"/> so subscribed webhook platforms receive them.
/// Alert forwarding never throws - delivery failures are isolated to the dispatcher.
/// </summary>
public sealed class WebhookAlertService : IGatewayAlertService
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ILogger<WebhookAlertService> _logger;

    public WebhookAlertService(NotificationDispatcher dispatcher, ILogger<WebhookAlertService> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public void AlertCircuitBreakerOpen(string clusterId, string? destinationId = null)
    {
        _logger.LogWarning("Circuit breaker opened: cluster={Cluster}, destination={Destination}", clusterId, destinationId);
        _dispatcher.EnqueueAlert(
            "CircuitBreakerOpen",
            clusterId,
            destinationId is null ? $"Circuit breaker opened on cluster '{clusterId}'."
                                  : $"Circuit breaker opened on cluster '{clusterId}', destination '{destinationId}'.",
            "Error");
    }

    public void AlertRetryExhausted(string clusterId, string routeId, int attempts, int statusCode)
    {
        _logger.LogWarning("Retries exhausted: cluster={Cluster}, route={Route}, attempts={Attempts}, status={Status}",
            clusterId, routeId, attempts, statusCode);
        _dispatcher.EnqueueAlert(
            "RetryExhausted",
            clusterId,
            $"All {attempts} retry attempts exhausted for route '{routeId}' (last status {statusCode}).",
            "Warning");
    }

    public void AlertWafBlock(string clientIp, string blockReason, string? uri = null)
    {
        _logger.LogWarning("WAF blocked request: ip={Ip}, reason={Reason}, uri={Uri}", clientIp, blockReason, uri);
        _dispatcher.EnqueueAlert(
            "WafBlock",
            clientIp,
            uri is null ? $"WAF blocked request from {clientIp}: {blockReason}."
                        : $"WAF blocked request from {clientIp} on {uri}: {blockReason}.",
            "Warning");
    }

    public void AlertProxyError(string clusterId, string? destinationId, string errorMessage, string? stackTrace = null)
    {
        _logger.LogWarning("Proxy error: cluster={Cluster}, destination={Destination}, error={Error}",
            clusterId, destinationId, errorMessage);
        _dispatcher.EnqueueAlert(
            "ProxyError",
            clusterId,
            destinationId is null ? errorMessage : $"Destination '{destinationId}': {errorMessage}",
            "Error");
    }

    public void AlertRateLimitExceeded(string clientIp, string? routeId = null)
    {
        _logger.LogWarning("Rate limit exceeded: ip={Ip}, route={Route}", clientIp, routeId);
        _dispatcher.EnqueueAlert(
            "RateLimitExceeded",
            routeId ?? clientIp,
            routeId is null ? $"Rate limit exceeded for client {clientIp}."
                            : $"Rate limit exceeded for client {clientIp} on route '{routeId}'.",
            "Warning");
    }

    public void AlertCustom(string alertType, string title, string message, string severity = "Info")
    {
        _logger.LogInformation("Custom alert {Type}: {Title}", alertType, title);
        _dispatcher.EnqueueAlert(alertType, title, message, severity);
    }
}
