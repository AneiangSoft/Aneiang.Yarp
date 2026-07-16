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
