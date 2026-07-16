using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>
/// Default implementation of <see cref="IGatewayPluginManager"/>.
/// Uses <see cref="IStateStore"/> for persistence (default: file system, replaceable).
/// </summary>
public class GatewayPluginManager : IGatewayPluginManager
{
    private const string StateKey = "plugin-states";
    private readonly Dictionary<string, IGatewayPlugin> _plugins = new();
    private readonly Dictionary<string, bool> _enabledPlugins = new();
    private readonly ILogger<GatewayPluginManager> _logger;
    private readonly IStateStore _stateStore;

    public GatewayPluginManager(
        IEnumerable<IGatewayPlugin> plugins,
        IConfiguration configuration,
        IStateStore stateStore,
        ILogger<GatewayPluginManager> logger)
    {
        _logger = logger;
        _stateStore = stateStore;

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
        // Blocking call - runs once at startup in singleton constructor
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

            // Fire-and-forget persistence - failure should not block the API response
            _ = _stateStore.SaveAsync(StateKey, state);
            _logger.LogDebug("Plugin state saved via IStateStore");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save plugin state via IStateStore");
        }
    }

    private void LoadState()
    {
        try
        {
            // Blocking call in singleton constructor - runs once at startup
            var state = _stateStore.LoadAsync<Dictionary<string, bool>>(StateKey).GetAwaiter().GetResult();
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
            _logger.LogWarning(ex, "Failed to load plugin state via IStateStore");
        }
    }
}
