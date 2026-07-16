using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

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
            description = p.Description,
            enabled = _manager.IsPluginEnabled(p.PluginId)
        });
        return Ok(ApiResponse.Ok(plugins));
    }

    /// <summary>Get a single plugin by ID.</summary>
    [HttpGet("{pluginId}")]
    public IActionResult GetPlugin(string pluginId)
    {
        var plugin = _manager.GetPlugin(pluginId);
        if (plugin == null)
            return NotFound(ApiResponse.Fail($"Plugin '{pluginId}' not found", 404));

        return Ok(ApiResponse.Ok(new
        {
            pluginId = plugin.PluginId,
            displayName = plugin.DisplayName,
            version = plugin.Version,
            description = plugin.Description,
            enabled = _manager.IsPluginEnabled(pluginId)
        }));
    }

    /// <summary>Enable or disable a plugin.</summary>
    [HttpPost("{pluginId}/toggle")]
    public IActionResult TogglePlugin(string pluginId, [FromBody] TogglePluginRequest request)
    {
        var plugin = _manager.GetPlugin(pluginId);
        if (plugin == null)
            return NotFound(ApiResponse.Fail($"Plugin '{pluginId}' not found", 404));

        _manager.SetPluginEnabled(pluginId, request.Enabled);
        return Ok(ApiResponse.Ok(new { pluginId, enabled = _manager.IsPluginEnabled(pluginId) }));
    }

    /// <summary>Reset all plugins to enabled state.</summary>
    [HttpPost("reset")]
    public IActionResult ResetPlugins()
    {
        foreach (var plugin in _manager.GetAllPlugins())
        {
            _manager.SetPluginEnabled(plugin.PluginId, true);
        }
        return Ok(ApiResponse.Ok("All plugins enabled"));
    }
}
