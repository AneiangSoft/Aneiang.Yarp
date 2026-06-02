using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services.Webhook;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Gateway alert service that sends notifications for gateway anomalies.
/// Supports circuit breaker open, retry exhaustion, WAF blocks, and proxy errors.
/// Uses the existing webhook infrastructure for delivery.
/// </summary>
public class GatewayAlertService : IGatewayAlertService
{
    private readonly DashboardOptions _options;
    private readonly ILogger<GatewayAlertService> _logger;
    private readonly WebhookNotificationService _webhookService;
    private readonly AlertHistoryStore _alertStore;

    private static readonly ConcurrentDictionary<string, DateTime> _alertCooldowns = new();
    private static readonly TimeSpan _defaultCooldown = TimeSpan.FromMinutes(5);

    public GatewayAlertService(
        IOptions<DashboardOptions> options,
        ILogger<GatewayAlertService> logger,
        WebhookNotificationService webhookService,
        AlertHistoryStore alertStore)
    {
        _options = options.Value;
        _logger = logger;
        _webhookService = webhookService;
        _alertStore = alertStore;
    }

    private void RecordAlert(AlertRecord record)
    {
        _alertStore.Add(record);
    }

    /// <inheritdoc />
    public void AlertCircuitBreakerOpen(string clusterId, string? destinationId = null)
    {
        if (!_options.AlertEnabled)
        {
            _logger.LogDebug("Alert suppressed: circuit breaker alert disabled");
            return;
        }

        var alertKey = $"circuit:{clusterId}:{destinationId ?? "any"}";
        if (!TryAcquireCooldown(alertKey, TimeSpan.FromMinutes(10)))
            return;

        _logger.LogWarning(
            "Circuit breaker opened for cluster '{ClusterId}' destination '{DestinationId}', sending alert",
            clusterId, destinationId ?? "any");

        var payload = new AlertPayload
        {
            AlertType = "CircuitBreakerOpen",
            Severity = "Warning",
            Title = "Circuit Breaker Opened",
            Message = $"Circuit breaker opened for cluster '{clusterId}'" +
                      (destinationId != null ? $" (destination: {destinationId})" : ""),
            Timestamp = DateTime.UtcNow,
            ClusterId = clusterId,
            DestinationId = destinationId
        };

        RecordAlert(new AlertRecord
        {
            AlertType = payload.AlertType,
            Severity = payload.Severity,
            Title = payload.Title,
            Message = payload.Message,
            Timestamp = payload.Timestamp,
            ClusterId = clusterId,
            DestinationId = destinationId
        });

        _ = _webhookService.NotifyAlertAsync(payload);
    }

    /// <inheritdoc />
    public void AlertRetryExhausted(string clusterId, string routeId, int attempts, int statusCode)
    {
        if (!_options.AlertEnabled)
        {
            _logger.LogDebug("Alert suppressed: retry alert disabled");
            return;
        }

        var alertKey = $"retry:{clusterId}:{routeId}";
        if (!TryAcquireCooldown(alertKey, _defaultCooldown))
            return;

        _logger.LogWarning(
            "Retry exhausted for route '{RouteId}' after {Attempts} attempts (status {StatusCode}), sending alert",
            routeId, attempts, statusCode);

        var payload = new AlertPayload
        {
            AlertType = "RetryExhausted",
            Severity = "Error",
            Title = "Retry Exhausted",
            Message = $"All {attempts} retry attempts failed for route '{routeId}' " +
                      $"(cluster: {clusterId}, last status: {statusCode})",
            Timestamp = DateTime.UtcNow,
            ClusterId = clusterId,
            RouteId = routeId,
            AttemptCount = attempts,
            LastStatusCode = statusCode
        };

        RecordAlert(new AlertRecord
        {
            AlertType = payload.AlertType,
            Severity = payload.Severity,
            Title = payload.Title,
            Message = payload.Message,
            Timestamp = payload.Timestamp,
            ClusterId = clusterId,
            RouteId = routeId,
            AttemptCount = attempts,
            LastStatusCode = statusCode
        });

        _ = _webhookService.NotifyAlertAsync(payload);
    }

    /// <inheritdoc />
    public void AlertWafBlock(string clientIp, string blockReason, string? uri = null)
    {
        if (!_options.AlertEnabled || !_options.AlertWafBlocks)
        {
            _logger.LogDebug("Alert suppressed: WAF alert disabled");
            return;
        }

        var alertKey = $"waf:{clientIp}";
        if (!TryAcquireCooldown(alertKey, TimeSpan.FromMinutes(15)))
            return;

        _logger.LogWarning(
            "WAF blocked request from {ClientIp} (reason: {Reason}, URI: {Uri}), sending alert",
            clientIp, blockReason, uri ?? "N/A");

        var payload = new AlertPayload
        {
            AlertType = "WafBlock",
            Severity = "Warning",
            Title = "WAF Blocked Request",
            Message = $"WAF blocked a request from {clientIp}. Reason: {blockReason}",
            Timestamp = DateTime.UtcNow,
            ClientIp = clientIp,
            BlockReason = blockReason,
            RequestUri = uri
        };

        RecordAlert(new AlertRecord
        {
            AlertType = payload.AlertType,
            Severity = payload.Severity,
            Title = payload.Title,
            Message = payload.Message,
            Timestamp = payload.Timestamp,
            ClientIp = clientIp,
            BlockReason = blockReason,
            RequestUri = uri
        });

        _ = _webhookService.NotifyAlertAsync(payload);
    }

    /// <inheritdoc />
    public void AlertProxyError(string clusterId, string? destinationId, string errorMessage, string? stackTrace = null)
    {
        if (!_options.AlertEnabled || !_options.AlertProxyErrors)
        {
            _logger.LogDebug("Alert suppressed: proxy error alert disabled");
            return;
        }

        var alertKey = $"proxy:{clusterId}:{destinationId ?? "any"}";
        if (!TryAcquireCooldown(alertKey, _defaultCooldown))
            return;

        _logger.LogError(
            "Proxy error for cluster '{ClusterId}' destination '{DestinationId}': {Error}, sending alert",
            clusterId, destinationId ?? "any", errorMessage);

        var payload = new AlertPayload
        {
            AlertType = "ProxyError",
            Severity = "Error",
            Title = "Proxy Error",
            Message = $"Proxy error for cluster '{clusterId}'" +
                      (destinationId != null ? $" (destination: {destinationId})" : "") +
                      $": {errorMessage}",
            Timestamp = DateTime.UtcNow,
            ClusterId = clusterId,
            DestinationId = destinationId,
            ErrorMessage = errorMessage,
            StackTrace = stackTrace
        };

        RecordAlert(new AlertRecord
        {
            AlertType = payload.AlertType,
            Severity = payload.Severity,
            Title = payload.Title,
            Message = payload.Message,
            Timestamp = payload.Timestamp,
            ClusterId = clusterId,
            DestinationId = destinationId,
            ErrorMessage = errorMessage
        });

        _ = _webhookService.NotifyAlertAsync(payload);
    }

    /// <inheritdoc />
    public void AlertRateLimitExceeded(string clientIp, string? routeId = null)
    {
        if (!_options.AlertEnabled || !_options.AlertRateLimitExceeded)
        {
            _logger.LogDebug("Alert suppressed: rate limit alert disabled");
            return;
        }

        var alertKey = $"ratelimit:{clientIp}";
        if (!TryAcquireCooldown(alertKey, _defaultCooldown))
            return;

        _logger.LogWarning(
            "Rate limit exceeded for {ClientIp} (route: {RouteId}), sending alert",
            clientIp, routeId ?? "global");

        var payload = new AlertPayload
        {
            AlertType = "RateLimitExceeded",
            Severity = "Warning",
            Title = "Rate Limit Exceeded",
            Message = $"Rate limit exceeded for {clientIp}" +
                      (routeId != null ? $" on route '{routeId}'" : ""),
            Timestamp = DateTime.UtcNow,
            ClientIp = clientIp,
            RouteId = routeId
        };

        RecordAlert(new AlertRecord
        {
            AlertType = payload.AlertType,
            Severity = payload.Severity,
            Title = payload.Title,
            Message = payload.Message,
            Timestamp = payload.Timestamp,
            ClientIp = clientIp,
            RouteId = routeId
        });

        _ = _webhookService.NotifyAlertAsync(payload);
    }

    /// <inheritdoc />
    public void AlertCustom(string alertType, string title, string message, string severity = "Info")
    {
        if (!_options.AlertEnabled)
        {
            _logger.LogDebug("Alert suppressed: custom alert disabled");
            return;
        }

        var payload = new AlertPayload
        {
            AlertType = alertType,
            Severity = severity,
            Title = title,
            Message = message,
            Timestamp = DateTime.UtcNow
        };

        RecordAlert(new AlertRecord
        {
            AlertType = payload.AlertType,
            Severity = payload.Severity,
            Title = payload.Title,
            Message = payload.Message,
            Timestamp = payload.Timestamp
        });

        _ = _webhookService.NotifyAlertAsync(payload);
    }

    private static bool TryAcquireCooldown(string key, TimeSpan cooldown)
    {
        var now = DateTime.UtcNow;
        if (_alertCooldowns.TryGetValue(key, out var lastAlert))
        {
            if (now - lastAlert < cooldown)
                return false;
        }

        _alertCooldowns[key] = now;
        return true;
    }
}

/// <summary>
/// Interface for gateway alert service.
/// </summary>
public interface IGatewayAlertService
{
    /// <summary>Send alert when circuit breaker opens.</summary>
    void AlertCircuitBreakerOpen(string clusterId, string? destinationId = null);

    /// <summary>Send alert when all retry attempts are exhausted.</summary>
    void AlertRetryExhausted(string clusterId, string routeId, int attempts, int statusCode);

    /// <summary>Send alert when WAF blocks a request.</summary>
    void AlertWafBlock(string clientIp, string blockReason, string? uri = null);

    /// <summary>Send alert when a proxy error occurs.</summary>
    void AlertProxyError(string clusterId, string? destinationId, string errorMessage, string? stackTrace = null);

    /// <summary>Send alert when rate limit is exceeded.</summary>
    void AlertRateLimitExceeded(string clientIp, string? routeId = null);

    /// <summary>Send a custom alert.</summary>
    void AlertCustom(string alertType, string title, string message, string severity = "Info");
}

/// <summary>
/// Alert payload sent to webhook providers.
/// </summary>
public class AlertPayload
{
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? ClusterId { get; set; }
    public string? DestinationId { get; set; }
    public string? RouteId { get; set; }
    public string? ClientIp { get; set; }
    public string? BlockReason { get; set; }
    public string? RequestUri { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public int? AttemptCount { get; set; }
    public int? LastStatusCode { get; set; }
}

/// <summary>
/// Extension methods for WebhookNotificationService to support alert payloads.
/// </summary>
public static class WebhookNotificationServiceExtensions
{
    /// <summary>
    /// Send an alert notification to all configured webhook URLs.
    /// </summary>
    public static async Task NotifyAlertAsync(this WebhookNotificationService service, AlertPayload payload)
    {
        var urls = GetWebhookUrls(service);
        if (urls == null || urls.Count == 0)
            return;

        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url))
                continue;

            await SendAlertAsync(service, url, payload);
        }
    }

    private static List<string>? GetWebhookUrls(WebhookNotificationService service)
    {
        var field = typeof(WebhookNotificationService).GetField(
            "_options",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (field?.GetValue(service) is DashboardOptions options)
        {
            return options.WebhookUrls;
        }
        return null;
    }

    private static async Task SendAlertAsync(WebhookNotificationService service, string url, AlertPayload payload)
    {
        var method = typeof(WebhookNotificationService).GetMethod(
            "SendAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (method != null)
        {
            var webhookPayload = new WebhookPayload
            {
                EventType = $"Alert:{payload.AlertType}",
                EventLabel = $"[{payload.Severity}] {payload.Title}",
                Target = payload.ClusterId ?? payload.RouteId ?? payload.ClientIp ?? "gateway",
                Timestamp = payload.Timestamp,
                Details = payload,
                GatewayName = Environment.MachineName
            };

            try
            {
                var task = (Task?)method.Invoke(service, new object[] { url, webhookPayload });
                if (task != null)
                    await task.ConfigureAwait(ConfigureAwaitOptions.None);
            }
            catch (Exception ex)
            {
                // Log webhook failures but don't throw - alerts are best-effort
                Debug.WriteLine($"[GatewayAlertService] Webhook delivery failed: {ex.Message}");
            }
        }
    }
}
