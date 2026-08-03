using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Provides the global activation state used when compiling plugin bindings.</summary>
public interface IPluginActivationState
{
    /// <summary>Returns whether bindings for the specified plugin may enter a gateway snapshot.</summary>
    bool IsPluginEnabled(string pluginId);
}

/// <summary>Default activation state used when no plugin host supplies global lifecycle state.</summary>
public sealed class AllowAllPluginActivationState : IPluginActivationState
{
    public bool IsPluginEnabled(string pluginId) => !string.IsNullOrWhiteSpace(pluginId);
}

/// <summary>Compiles persisted plugin bindings with an immutable YARP configuration.</summary>
public interface IGatewaySnapshotCompiler
{
    Task<GatewaySnapshot> CompileAsync(
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters,
        long version,
        CancellationToken ct = default,
        IReadOnlyList<PluginBindingEntity>? candidateBindings = null);
}

/// <summary>Atomically publishes compiled gateway snapshots.</summary>
public interface IGatewaySnapshotPublisher
{
    GatewaySnapshot Current { get; }
    GatewaySnapshot Publish(GatewaySnapshot snapshot);
}
