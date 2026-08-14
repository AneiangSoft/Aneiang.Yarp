using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Cluster session affinity adapter: compiles to YARP native SessionAffinity field.</summary>
public static class SessionAffinityAdapter
{
    public const string PluginId = "native.cluster.session-affinity";

    public static NativePluginAdapterDescriptor Descriptor { get; } = new(PluginId, "Cluster Session Affinity", PluginBindingScope.Cluster);

    public static ClusterConfig Apply(ClusterConfig cluster, ClusterSessionAffinityConfig value) =>
        cluster with { SessionAffinity = ToSessionAffinity(value) };

    private static SessionAffinityConfig ToSessionAffinity(ClusterSessionAffinityConfig value)
    {
        if (value.Enabled && string.IsNullOrWhiteSpace(value.Policy))
            throw new ArgumentException("Policy is required when session affinity is enabled.");
        return new SessionAffinityConfig
        {
            Enabled = value.Enabled,
            Policy = value.Policy,
            FailurePolicy = value.FailurePolicy,
            AffinityKeyName = NativeAdapterHelpers.Required(value.AffinityKeyName, "AffinityKeyName")
        };
    }
}

/// <summary>Configuration model for <see cref="SessionAffinityAdapter"/>.</summary>
public sealed class ClusterSessionAffinityConfig
{
    public bool Enabled { get; init; }
    public string? Policy { get; init; }
    public string? FailurePolicy { get; init; }
    public string? AffinityKeyName { get; init; }
}
