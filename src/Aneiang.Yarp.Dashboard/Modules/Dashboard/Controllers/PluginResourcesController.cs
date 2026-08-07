using Aneiang.Yarp.Dashboard.Infrastructure.I18n;
using Aneiang.Yarp.Plugins;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Exposes per-plugin resource usage statistics (memory, requests, errors, health).
/// </summary>
[ApiController]
[Route("api/plugin-resources")]
public sealed class PluginResourcesController(IPluginResourceMonitor monitor) : ControllerBase
{
    private string ResolveLocale() =>
        Request.Cookies["dashboard_locale"] == "en-US" ? "en-US" : "zh-CN";

    /// <summary>Get resource usage for all plugins with localized display names.</summary>
    [HttpGet]
    public IActionResult GetAll()
    {
        var usage = monitor.GetAllUsage();
        var totals = monitor.GetTotals();
        var locale = ResolveLocale();
        var dict = DashboardI18n.GetDict(locale);

        var localizedItems = usage.Select(u => new
        {
            u.PluginId,
            displayName = dict.TryGetValue($"plugin.name.{u.PluginId}", out var name) ? name : u.DisplayName,
            u.Enabled,
            u.IsBuiltIn,
            u.MemoryBytes,
            u.RequestCount,
            u.ErrorCount,
            u.AverageLatencyMs,
            u.ActiveResources,
            u.TotalResources,
            u.OverallHealth,
            u.Uptime,
            u.LastUpdated,
            u.CustomStatistics
        });

        return Ok(new { items = localizedItems, totals });
    }

    /// <summary>Get resource usage for a specific plugin.</summary>
    [HttpGet("{pluginId}")]
    public IActionResult GetOne(string pluginId)
    {
        var usage = monitor.GetUsage(pluginId);
        if (usage == null)
            return NotFound(new { message = $"Plugin '{pluginId}' not found" });
        return Ok(usage);
    }

    /// <summary>Get aggregated totals across all plugins.</summary>
    [HttpGet("totals")]
    public IActionResult GetTotals()
    {
        return Ok(monitor.GetTotals());
    }
}
