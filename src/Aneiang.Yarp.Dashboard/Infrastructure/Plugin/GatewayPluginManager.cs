using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>
/// Manages gateway plugins lifecycle: discovery, configuration, and pipeline integration.
/// </summary>
public interface IGatewayPluginManager
{
    /// <summary>Get all registered plugins.</summary>
    IReadOnlyList<IGatewayPlugin> GetAllPlugins();

    /// <summary>Get a plugin by its ID.</summary>
    IGatewayPlugin? GetPlugin(string pluginId);

    /// <summary>Check if a plugin is enabled.</summary>
    bool IsPluginEnabled(string pluginId);

    /// <summary>Enable or disable a plugin at runtime.</summary>
    void SetPluginEnabled(string pluginId, bool enabled);

    /// <summary>Save current plugin states to persistent storage.</summary>
    void SaveState();
}

/// <summary>
/// Default implementation of <see cref="IGatewayPluginManager"/>.
/// </summary>
public class GatewayPluginManager : IGatewayPluginManager
{
    private readonly Dictionary<string, IGatewayPlugin> _plugins = new();
    private readonly Dictionary<string, bool> _enabledPlugins = new();
    private readonly ILogger<GatewayPluginManager> _logger;
    private readonly string _stateFilePath;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public GatewayPluginManager(
        IEnumerable<IGatewayPlugin> plugins,
        IConfiguration configuration,
        IHostEnvironment hostEnv,
        ILogger<GatewayPluginManager> logger)
    {
        _logger = logger;
        _stateFilePath = Path.Combine(hostEnv.ContentRootPath, "plugin-states.json");

        var section = configuration.GetSection("Gateway:Dashboard:Plugins");

        foreach (var plugin in plugins)
        {
            _plugins[plugin.PluginId] = plugin;

            // Check if plugin is enabled (default: true if not specified)
            if (section[$"{plugin.PluginId}:Enabled"] is string enabledStr)
            {
                _enabledPlugins[plugin.PluginId] = bool.TryParse(enabledStr, out var e) && e;
            }
            else
            {
                _enabledPlugins[plugin.PluginId] = true;
            }

            _logger.LogDebug(
                "Plugin '{PluginName}' v{Version} ({PluginId}) registered, enabled: {Enabled}",
                plugin.DisplayName, plugin.Version, plugin.PluginId,
                _enabledPlugins[plugin.PluginId]);
        }

        // Override defaults with persisted state (if exists)
        LoadState();
    }

    /// <inheritdoc />
    public IReadOnlyList<IGatewayPlugin> GetAllPlugins() => _plugins.Values.ToList().AsReadOnly();

    /// <inheritdoc />
    public IGatewayPlugin? GetPlugin(string pluginId) =>
        _plugins.GetValueOrDefault(pluginId);

    /// <inheritdoc />
    public bool IsPluginEnabled(string pluginId)
    {
        if (!_enabledPlugins.TryGetValue(pluginId, out var enabled))
            return false;
        return enabled;
    }

    /// <inheritdoc />
    public void SetPluginEnabled(string pluginId, bool enabled)
    {
        if (!_plugins.ContainsKey(pluginId))
        {
            _logger.LogWarning("Cannot enable/disable unknown plugin: {PluginId}", pluginId);
            return;
        }

        _enabledPlugins[pluginId] = enabled;
        _logger.LogInformation(
            "Plugin '{PluginId}' {Action}",
            pluginId, enabled ? "enabled" : "disabled");

        SaveState();
    }

    /// <inheritdoc />
    public void SaveState()
    {
        try
        {
            var state = new Dictionary<string, bool>();
            foreach (var kvp in _enabledPlugins)
                state[kvp.Key] = kvp.Value;

            var json = JsonSerializer.Serialize(state, _jsonOptions);
            File.WriteAllText(_stateFilePath, json);
            _logger.LogDebug("Plugin state saved to {Path}", _stateFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save plugin state to {Path}", _stateFilePath);
        }
    }

    private void LoadState()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
                return;

            var json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<Dictionary<string, bool>>(json, _jsonOptions);
            if (state == null) return;

            foreach (var kvp in state)
            {
                if (_plugins.ContainsKey(kvp.Key))
                {
                    _enabledPlugins[kvp.Key] = kvp.Value;
                    _logger.LogInformation("Plugin '{PluginId}' state loaded from persistence: {Enabled}",
                        kvp.Key, kvp.Value);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load plugin state from {Path}", _stateFilePath);
        }
    }
}
