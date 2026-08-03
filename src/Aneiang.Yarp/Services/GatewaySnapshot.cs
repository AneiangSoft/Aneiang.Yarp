using System.Collections.Immutable;
using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Immutable, fully compiled gateway configuration ready for atomic publication.</summary>
public sealed record GatewaySnapshot(
    long Version,
    DateTimeOffset CreatedAt,
    ImmutableArray<RouteConfig> Routes,
    ImmutableArray<ClusterConfig> Clusters,
    ImmutableDictionary<string, ImmutableArray<PluginBindingSnapshot>> RoutePlugins,
    ImmutableDictionary<string, ImmutableArray<PluginBindingSnapshot>> ClusterPlugins,
    ImmutableDictionary<string, RouteExecutionPlan> RouteExecutionPlans,
    ImmutableDictionary<string, ClusterExecutionPlan> ClusterExecutionPlans)
{
    public static GatewaySnapshot Empty { get; } = new(
        0, DateTimeOffset.UnixEpoch, [], [],
        ImmutableDictionary<string, ImmutableArray<PluginBindingSnapshot>>.Empty,
        ImmutableDictionary<string, ImmutableArray<PluginBindingSnapshot>>.Empty,
        ImmutableDictionary<string, RouteExecutionPlan>.Empty,
        ImmutableDictionary<string, ClusterExecutionPlan>.Empty);

    public bool TryGetRouteExecutionPlan(string routeId, out RouteExecutionPlan? plan) =>
        RouteExecutionPlans.TryGetValue(routeId, out plan);

    public bool TryGetClusterExecutionPlan(string clusterId, out ClusterExecutionPlan? plan) =>
        ClusterExecutionPlans.TryGetValue(clusterId, out plan);
}

/// <summary>Immutable plugin configuration captured in a gateway snapshot.</summary>
public sealed record PluginBindingSnapshot(
    string BindingId,
    string PluginId,
    PluginBindingScope Scope,
    string ScopeId,
    string ConfigJson,
    int SchemaVersion,
    long ConfigVersion,
    int Order);
