using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>
/// Alert history and management API.
/// </summary>
[Route("api/alerts")]
[ApiController]
public class AlertsController : ControllerBase
{
    private readonly AlertHistoryStore _alertStore;
    private readonly GatewayAlertService _alertService;

    public AlertsController(
        AlertHistoryStore alertStore,
        GatewayAlertService alertService)
    {
        _alertStore = alertStore;
        _alertService = alertService;
    }

    /// <summary>Get recent alert history.</summary>
    [HttpGet]
    public IActionResult GetAlerts([FromQuery] int count = 100)
    {
        var alerts = _alertStore.GetRecent(count);
        return Ok(new { code = 200, data = new { entries = alerts, total = _alertStore.Count } });
    }

    /// <summary>Clear all alert history.</summary>
    [HttpDelete]
    public IActionResult ClearAlerts()
    {
        _alertStore.Clear();
        return Ok(new { code = 200, message = "Alert history cleared" });
    }

    /// <summary>
    /// Test alert — fires a sample alert directly into the history store.
    /// Always works regardless of AlertEnabled setting.
    /// </summary>
    [HttpPost("test")]
    public IActionResult SendTestAlert([FromBody] TestAlertRequest? request)
    {
        var record = new AlertRecord
        {
            AlertType = request?.AlertType ?? "TestAlert",
            Severity = request?.Severity ?? "Info",
            Title = request?.Title ?? "Test Alert",
            Message = request?.Message ?? "This is a test alert from the dashboard.",
            Timestamp = DateTime.UtcNow,
            ClusterId = request?.ClusterId,
            RouteId = request?.RouteId,
            ClientIp = request?.ClientIp ?? "127.0.0.1"
        };

        // Always add to history so the Alerts page always has data to show
        _alertStore.Add(record);

        // Also fire webhook if configured (respects cooldown)
        _alertService.AlertCustom(
            record.AlertType,
            record.Title,
            record.Message,
            record.Severity);

        return Ok(new
        {
            code = 200,
            message = "Test alert sent",
            data = new { id = record.Id, severity = record.Severity, timestamp = record.Timestamp }
        });
    }

    /// <summary>
    /// Fire a circuit breaker open alert for testing purposes.
    /// </summary>
    [HttpPost("test/circuit-breaker")]
    public IActionResult TestCircuitBreakerAlert([FromBody] TestCircuitBreakerRequest? request)
    {
        var clusterId = request?.ClusterId ?? "TestCluster";
        var destId = request?.DestinationId;

        var record = new AlertRecord
        {
            AlertType = "CircuitBreakerOpen",
            Severity = "Warning",
            Title = "Circuit Breaker Opened (Test)",
            Message = $"Circuit breaker opened for cluster '{clusterId}'" +
                      (destId != null ? $" (destination: {destId})" : ""),
            Timestamp = DateTime.UtcNow,
            ClusterId = clusterId,
            DestinationId = destId
        };

        _alertStore.Add(record);
        _alertService.AlertCircuitBreakerOpen(clusterId, destId);

        return Ok(new { code = 200, message = "Circuit breaker alert fired", data = new { id = record.Id } });
    }

    /// <summary>
    /// Fire a WAF block alert for testing purposes.
    /// </summary>
    [HttpPost("test/waf-block")]
    public IActionResult TestWafBlockAlert([FromBody] TestWafBlockRequest? request)
    {
        var clientIp = request?.ClientIp ?? "192.168.1.100";
        var reason = request?.Reason ?? "SqlInjectionBlocked";
        var uri = request?.Uri ?? "/test/?q=SELECT * FROM users";

        var record = new AlertRecord
        {
            AlertType = "WafBlock",
            Severity = "Warning",
            Title = "WAF Blocked Request (Test)",
            Message = $"WAF blocked a request from {clientIp}. Reason: {reason}",
            Timestamp = DateTime.UtcNow,
            ClientIp = clientIp,
            BlockReason = reason,
            RequestUri = uri
        };

        _alertStore.Add(record);
        _alertService.AlertWafBlock(clientIp, reason, uri);

        return Ok(new { code = 200, message = "WAF block alert fired", data = new { id = record.Id } });
    }

    /// <summary>
    /// Fire a retry exhausted alert for testing purposes.
    /// </summary>
    [HttpPost("test/retry-exhausted")]
    public IActionResult TestRetryExhaustedAlert([FromBody] TestRetryExhaustedRequest? request)
    {
        var routeId = request?.RouteId ?? "TestRoute";
        var clusterId = request?.ClusterId ?? "TestCluster";
        var attempts = request?.Attempts ?? 3;
        var statusCode = request?.StatusCode ?? 502;

        var record = new AlertRecord
        {
            AlertType = "RetryExhausted",
            Severity = "Error",
            Title = "Retry Exhausted (Test)",
            Message = $"All {attempts} retry attempts failed for route '{routeId}' (cluster: {clusterId}, last status: {statusCode})",
            Timestamp = DateTime.UtcNow,
            ClusterId = clusterId,
            RouteId = routeId,
            AttemptCount = attempts,
            LastStatusCode = statusCode
        };

        _alertStore.Add(record);
        _alertService.AlertRetryExhausted(clusterId, routeId, attempts, statusCode);

        return Ok(new { code = 200, message = "Retry exhausted alert fired", data = new { id = record.Id } });
    }

    /// <summary>Get alert summary statistics.</summary>
    [HttpGet("summary")]
    public IActionResult GetAlertSummary()
    {
        var alerts = _alertStore.GetRecent(500);
        var severityCounts = alerts.GroupBy(a => a.Severity)
            .ToDictionary(g => g.Key, g => g.Count());
        var typeCounts = alerts.GroupBy(a => a.AlertType)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .ToDictionary(g => g.Key, g => g.Count());

        return Ok(new
        {
            code = 200,
            data = new
            {
                total = _alertStore.Count,
                severityCounts,
                typeCounts,
                recentCount = alerts.Count,
                lastAlert = alerts.FirstOrDefault()?.Timestamp
            }
        });
    }
}

public class TestAlertRequest
{
    public string? AlertType { get; set; }
    public string? Title { get; set; }
    public string? Message { get; set; }
    public string? Severity { get; set; }
    public string? ClusterId { get; set; }
    public string? RouteId { get; set; }
    public string? ClientIp { get; set; }
}

public class TestCircuitBreakerRequest
{
    public string? ClusterId { get; set; }
    public string? DestinationId { get; set; }
}

public class TestWafBlockRequest
{
    public string? ClientIp { get; set; }
    public string? Reason { get; set; }
    public string? Uri { get; set; }
}

public class TestRetryExhaustedRequest
{
    public string? RouteId { get; set; }
    public string? ClusterId { get; set; }
    public int? Attempts { get; set; }
    public int? StatusCode { get; set; }
}
