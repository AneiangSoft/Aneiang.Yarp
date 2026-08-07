namespace Aneiang.Yarp.Plugins;

/// <summary>
/// Provides plugin manifests independently of runtime plugin activation.
/// A future plugin.json scanner can implement this contract without loading plugin assemblies.
/// </summary>
public interface IPluginManifestCatalog
{
    IReadOnlyList<PluginManifest> GetAllManifests();
    PluginManifest? GetManifest(string pluginId);
}

/// <summary>Registration status for external (AssemblyLoadContext) plugins.</summary>
public enum ExternalPluginRegistrationStatus
{
    Discovered,
    Loaded,
    LoadFailed,
    InvalidManifest,
    DependencyUnsatisfied,
    Disabled,
    UnloadPending,
    Unloaded
}

/// <summary>Result of a plugin state change operation.</summary>
public sealed record PluginStateChangeResult(bool Succeeded, string? Error = null)
{
    public static PluginStateChangeResult Success { get; } = new(true);
}

/// <summary>Runtime state snapshot for a plugin.</summary>
public sealed record PluginRuntimeState(
    PluginManifest Manifest,
    bool Enabled,
    bool IsBuiltIn,
    IReadOnlyList<string> MissingDependencies,
    IReadOnlyList<string> BindingTargets,
    string Health,
    ExternalPluginRegistrationStatus? RegistrationStatus = null,
    string? RegistrationError = null,
    DateTimeOffset? EnabledAt = null);

/// <summary>
/// Manages gateway plugins lifecycle: discovery, configuration, and pipeline integration.
/// </summary>
public interface IGatewayPluginManager : IPluginManifestCatalog
{
    /// <summary>Get all registered runtime plugins.</summary>
    IReadOnlyList<IGatewayPlugin> GetAllPlugins();

    /// <summary>Get a plugin by its ID.</summary>
    IGatewayPlugin? GetPlugin(string pluginId);

    /// <summary>Check if a plugin is enabled.</summary>
    bool IsPluginEnabled(string pluginId);

    /// <summary>Validate an enable or disable operation without changing state.</summary>
    PluginStateChangeResult ValidatePluginStateChange(string pluginId, bool enabled);

    /// <summary>Enable or disable a plugin after dependency and binding safety checks.</summary>
    PluginStateChangeResult SetPluginEnabled(string pluginId, bool enabled);

    /// <summary>Returns runtime state, dependencies, bindings and resource declarations for all manifests.</summary>
    IReadOnlyList<PluginRuntimeState> GetPluginStates();

    /// <summary>Save current plugin states to persistent storage.</summary>
    void SaveState();

    /// <summary>Install an external plugin from a source directory.</summary>
    PluginStateChangeResult InstallPlugin(string sourceDirectory);

    /// <summary>Uninstall an external plugin (must be disabled and unbound).</summary>
    PluginStateChangeResult UninstallPlugin(string pluginId);

    /// <summary>Upgrade an external plugin from a source directory.</summary>
    PluginStateChangeResult UpgradePlugin(string pluginId, string sourceDirectory);
}
