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

    /// <summary>Test alert - fire a sample alert to verify webhook delivery.</summary>
    [HttpPost("test")]
    public IActionResult SendTestAlert([FromBody] TestAlertRequest? request)
    {
        _alertService.AlertCustom(
            request?.AlertType ?? "TestAlert",
            request?.Title ?? "Test Alert",
            request?.Message ?? "This is a test alert from the dashboard.",
            request?.Severity ?? "Info");
        return Ok(new { code = 200, message = "Test alert sent" });
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
}
