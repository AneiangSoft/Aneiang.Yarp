using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Plugins;

/// <summary>Static metadata describing a gateway plugin without activating its runtime implementation.</summary>
public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    IReadOnlyList<PluginScope> Scopes,
    IReadOnlyList<PluginCapability> Capabilities,
    int Order,
    PluginResourceRequirements Resources,
    IReadOnlyList<PluginSchemaReference> Schemas,
    string Description = "")
{
    /// <summary>Other plugins that must be enabled before this plugin can be enabled.</summary>
    public IReadOnlyList<PluginDependency> Dependencies { get; init; } = [];

    /// <summary>Entry assembly path relative to plugin.json for an external plugin.</summary>
    public string? EntryAssembly { get; init; }

    /// <summary>Assembly-qualified or full type name implementing <see cref="IGatewayPlugin"/>.</summary>
    public string? EntryType { get; init; }
}

/// <summary>A versioned dependency on another installed plugin.</summary>
public sealed record PluginDependency(string PluginId, string? MinimumVersion = null);

/// <summary>Target types to which a plugin configuration can be bound.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PluginScope
{
    Route,
    Cluster
}

/// <summary>Host integration points used by a plugin.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PluginCapability
{
    RequestMiddleware,
    ProxyPipeline,
    Dashboard,
    BackgroundService
}

/// <summary>Resources a plugin may consume while active.</summary>
public sealed record PluginResourceRequirements(
    bool RequestMiddleware = false,
    bool BackgroundServices = false,
    bool Database = false,
    bool NetworkConnections = false);

/// <summary>Reference to a versioned configuration schema supplied by a plugin.</summary>
public sealed record PluginSchemaReference(
    int Version,
    string Reference,
    string ConfigJsonSchema = "{}");

/// <summary>Transforms persisted plugin configuration between two adjacent schema versions.</summary>
public interface IPluginConfigurationMigrator
{
    string PluginId { get; }
    int FromVersion { get; }
    int ToVersion { get; }
    bool TryMigrate(string configJson, out string migratedConfigJson, out string error);
}

/// <summary>Service that coordinates plugin configuration migrations.</summary>
public interface IPluginConfigurationMigrationService
{
    bool TryMigrate(string pluginId, int fromVersion, int toVersion, string configJson, out string migratedConfigJson, out string error);
}
