using System.Text.Json.Serialization;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage.Entities;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

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

/// <summary>
/// Provides plugin manifests independently of runtime plugin activation.
/// A future plugin.json scanner can implement this contract without loading plugin assemblies.
/// </summary>
public interface IPluginManifestCatalog
{
    IReadOnlyList<PluginManifest> GetAllManifests();
    PluginManifest? GetManifest(string pluginId);
}

/// <summary>Manifest catalog backed by the built-in, DI-registered plugins.</summary>
public sealed class BuiltInPluginManifestCatalog : IPluginManifestCatalog
{
    private readonly IReadOnlyDictionary<string, PluginManifest> _manifests;

    public BuiltInPluginManifestCatalog(IEnumerable<IGatewayPlugin> plugins)
    {
        var runtimeManifests = plugins.Select(plugin => plugin.Manifest);
        var nativeManifests = NativePluginAdapters.Catalog.Select(CreateNativeManifest);

        _manifests = runtimeManifests
            .Concat(nativeManifests)
            .ToDictionary(manifest => manifest.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static PluginManifest CreateNativeManifest(NativePluginAdapterDescriptor adapter) => new(
        adapter.PluginId,
        adapter.DisplayName,
        "1.0",
        [adapter.Scope == PluginBindingScope.Route ? PluginScope.Route : PluginScope.Cluster],
        [],
        0,
        new PluginResourceRequirements(),
        [new PluginSchemaReference(1, $"native://{adapter.PluginId}/schema/1", GetNativeSchema(adapter.PluginId))],
        "Built-in adapter that compiles configuration directly to native YARP fields.");

    private static string GetNativeSchema(string pluginId) => pluginId switch
    {
        NativePluginAdapters.RouteCors => """{"type":"object","additionalProperties":false,"required":["CorsPolicy"],"properties":{"CorsPolicy":{"type":"string","minLength":1,"title":"CORS policy"}}}""",
        NativePluginAdapters.RouteRateLimit => """{"type":"object","additionalProperties":false,"required":["RateLimiterPolicy"],"properties":{"RateLimiterPolicy":{"type":"string","minLength":1,"title":"Rate limiter policy"}}}""",
        NativePluginAdapters.RouteCompression => """{"type":"object","additionalProperties":false,"properties":{"Enabled":{"type":"boolean","default":true,"title":"Forward compressed responses"}}}""",
        _ => "{}"
    };

    public IReadOnlyList<PluginManifest> GetAllManifests() => _manifests.Values.ToArray();

    public PluginManifest? GetManifest(string pluginId) =>
        string.IsNullOrWhiteSpace(pluginId) ? null : _manifests.GetValueOrDefault(pluginId.Trim());
}
