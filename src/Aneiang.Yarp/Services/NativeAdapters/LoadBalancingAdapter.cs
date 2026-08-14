using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Cluster load balancing adapter: compiles to YARP native LoadBalancingPolicy field.</summary>
public static class LoadBalancingAdapter
{
    public const string PluginId = "native.cluster.load-balancing";

    public static NativePluginAdapterDescriptor Descriptor { get; } = new(PluginId, "Cluster Load Balancing", PluginBindingScope.Cluster);

    public static ClusterConfig Apply(ClusterConfig cluster, ClusterLoadBalancingConfig value) =>
        cluster with { LoadBalancingPolicy = NativeAdapterHelpers.Required(value.LoadBalancingPolicy, "LoadBalancingPolicy") };
}

/// <summary>Configuration model for <see cref="LoadBalancingAdapter"/>.</summary>
public sealed class ClusterLoadBalancingConfig
{
    public string? LoadBalancingPolicy { get; init; }
}
