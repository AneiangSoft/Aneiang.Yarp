using Aneiang.Yarp.Dashboard.Plugins;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>
/// Plugin management API.
/// </summary>
[Route("api/plugins")]
[ApiController]
public class PluginsController : ControllerBase
{
    private readonly IGatewayPluginManager _manager;

    public PluginsController(IGatewayPluginManager manager)
    {
        _manager = manager;
    }

    /// <summary>Get all registered plugins.</summary>
    [HttpGet]
    public IActionResult GetPlugins()
    {
        var plugins = _manager.GetAllPlugins().Select(p => new
        {
            pluginId = p.PluginId,
            displayName = p.DisplayName,
            version = p.Version,
            description = GetPluginDescription(p),
            enabled = _manager.IsPluginEnabled(p.PluginId)
        });
        return Ok(new { code = 200, data = plugins });
    }

    private static string GetPluginDescription(IGatewayPlugin plugin)
    {
        return plugin.PluginId switch
        {
            "circuit-breaker" => "Monitors backend service health and trips circuits when failures exceed threshold.",
            "request-retry" => "Automatically retries failed proxy requests with configurable backoff strategy.",
            "waf" => "Web Application Firewall: blocks SQL injection, XSS, path traversal, and other attacks.",
            _ => string.Empty
        };
    }

    /// <summary>Get a single plugin by ID.</summary>
    [HttpGet("{pluginId}")]
    public IActionResult GetPlugin(string pluginId)
    {
        var plugin = _manager.GetPlugin(pluginId);
        if (plugin == null)
            return NotFound(new { code = 404, message = $"Plugin '{pluginId}' not found" });

        return Ok(new
        {
            code = 200,
            data = new
            {
                pluginId = plugin.PluginId,
                displayName = plugin.DisplayName,
                version = plugin.Version,
                description = GetPluginDescription(plugin),
                enabled = _manager.IsPluginEnabled(pluginId)
            }
        });
    }

    /// <summary>Enable or disable a plugin.</summary>
    [HttpPost("{pluginId}/toggle")]
    public IActionResult TogglePlugin(string pluginId, [FromBody] TogglePluginRequest request)
    {
        var plugin = _manager.GetPlugin(pluginId);
        if (plugin == null)
            return NotFound(new { code = 404, message = $"Plugin '{pluginId}' not found" });

        _manager.SetPluginEnabled(pluginId, request.Enabled);
        return Ok(new { code = 200, data = new { pluginId, enabled = _manager.IsPluginEnabled(pluginId) } });
    }

    /// <summary>Reset all plugins to enabled state.</summary>
    [HttpPost("reset")]
    public IActionResult ResetPlugins()
    {
        foreach (var plugin in _manager.GetAllPlugins())
        {
            _manager.SetPluginEnabled(plugin.PluginId, true);
        }
        return Ok(new { code = 200, message = "All plugins enabled" });
    }
}

public class TogglePluginRequest
{
    public bool Enabled { get; set; }
}
