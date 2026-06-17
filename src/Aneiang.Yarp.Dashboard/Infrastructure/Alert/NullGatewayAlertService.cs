using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Alert;

/// <summary>
/// Default no-op implementation of <see cref="IGatewayAlertService"/> that logs
/// alerts at the configured level. Suitable when no webhook or external sink is configured.
/// </summary>
public class NullGatewayAlertService : IGatewayAlertService
{
    private readonly ILogger<NullGatewayAlertService> _logger;
    private readonly DashboardOptions _options;

    public NullGatewayAlertService(ILogger<NullGatewayAlertService> logger, IOptions<DashboardOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public void AlertCircuitBreakerOpen(string clusterId, string? destinationId = null) =>
        Log("CircuitBreakerOpen", clusterId, destinationId);

    public void AlertRetryExhausted(string clusterId, string routeId, int attempts, int statusCode) =>
        Log("RetryExhausted", clusterId, $"route={routeId}, attempts={attempts}, status={statusCode}");

    public void AlertWafBlock(string clientIp, string blockReason, string? uri = null) =>
        Log("WafBlock", clientIp, $"reason={blockReason}, uri={uri}");

    public void AlertProxyError(string clusterId, string? destinationId, string errorMessage, string? stackTrace = null) =>
        Log("ProxyError", clusterId, $"dest={destinationId}, error={errorMessage}");

    public void AlertRateLimitExceeded(string clientIp, string? routeId = null) =>
        Log("RateLimitExceeded", clientIp, $"route={routeId}");

    public void AlertCustom(string alertType, string title, string message, string severity = "Info")
    {
        var level = severity?.ToLowerInvariant() switch
        {
            "error"   => LogLevel.Error,
            "warning" => LogLevel.Warning,
            _         => LogLevel.Information
        };
        _logger.Log(level, "[Alert:{Type}] {Title} - {Message}", alertType, title, message);
    }

    private void Log(string type, string subject, string? details = null)
    {
        _logger.LogWarning("[Alert:{Type}] {Subject} {Details}", type, subject, details ?? string.Empty);
    }
}
