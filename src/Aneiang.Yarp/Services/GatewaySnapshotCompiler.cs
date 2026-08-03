using System.Collections.Immutable;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Default compiler that captures enabled bindings without activating plugin modules.</summary>
public sealed class GatewaySnapshotCompiler : IGatewaySnapshotCompiler
{
    private readonly IPluginConfigurationRepository _plugins;
    private readonly IPluginActivationState _activationState;
    private readonly IReadOnlyList<IRoutePluginCompiler> _routeCompilers;
    private readonly IReadOnlyList<IClusterPluginCompiler> _clusterCompilers;
    private readonly IRouteRepository? _routes;
    private readonly IClusterRepository? _clusters;

    public GatewaySnapshotCompiler(
        IPluginConfigurationRepository plugins,
        IPluginActivationState? activationState = null,
        IEnumerable<IRoutePluginCompiler>? routeCompilers = null,
        IEnumerable<IClusterPluginCompiler>? clusterCompilers = null,
        IRouteRepository? routes = null,
        IClusterRepository? clusters = null)
    {
        _plugins = plugins;
        _activationState = activationState ?? new AllowAllPluginActivationState();
        _routeCompilers = BuildCompilerPipeline(routeCompilers, static compiler => compiler.Order);
        _clusterCompilers = BuildCompilerPipeline(clusterCompilers, static compiler => compiler.Order);
        _routes = routes;
        _clusters = clusters;
    }

    public async Task<GatewaySnapshot> CompileAsync(
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters,
        long version,
        CancellationToken ct = default,
        IReadOnlyList<PluginBindingEntity>? candidateBindings = null)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(clusters);
        if (version < 0) throw new ArgumentOutOfRangeException(nameof(version));

        var routeIds = routes.Select(x => x.RouteId).ToHashSet(StringComparer.Ordinal);
        var clusterIds = clusters.Select(x => x.ClusterId).ToHashSet(StringComparer.Ordinal);
        var routeUidMap = _routes is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : (await _routes.GetAllRoutesAsync(ct)).Where(x => !string.IsNullOrWhiteSpace(x.RouteUid))
                .ToDictionary(x => x.RouteUid, x => x.RouteId, StringComparer.Ordinal);
        var clusterUidMap = _clusters is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : (await _clusters.GetAllClustersAsync(ct)).Where(x => !string.IsNullOrWhiteSpace(x.ClusterUid))
                .ToDictionary(x => x.ClusterUid, x => x.ClusterId, StringComparer.Ordinal);
        var sourceBindings = candidateBindings ?? await _plugins.GetBindingsAsync(ct);
        var bindings = sourceBindings
            .Where(x => x.Enabled)
            .Where(x => _activationState.IsPluginEnabled(x.PluginId))
            .Select(x => ToSnapshot(x, routeUidMap, clusterUidMap))
            .Where(x => x.Scope == PluginBindingScope.Route ? routeIds.Contains(x.ScopeId) : clusterIds.Contains(x.ScopeId))
            .ToArray();

        var routeBindings = Group(bindings, PluginBindingScope.Route);
        var clusterBindings = Group(bindings, PluginBindingScope.Cluster);
        var compiledRoutes = ImmutableArray.CreateBuilder<RouteConfig>(routes.Count);
        var routePlans = ImmutableDictionary.CreateBuilder<string, RouteExecutionPlan>(StringComparer.Ordinal);
        foreach (var route in routes)
        {
            var current = route;
            var compiledPlugins = new List<CompiledRoutePlugin>();
            if (routeBindings.TryGetValue(route.RouteId, out var targetBindings))
            {
                foreach (var binding in targetBindings)
                {
                    foreach (var compiler in _routeCompilers.Where(x => x.CanCompile(binding.PluginId)))
                    {
                        var compiled = compiler.Compile(binding, current);
                        current = compiled.Route;
                        compiledPlugins.Add(compiled);
                    }
                }
            }
            compiledRoutes.Add(current);
            routePlans[route.RouteId] = new RouteExecutionPlan(route.RouteId, PluginExecutionPlanOrdering.Order(compiledPlugins));
        }

        var compiledClusters = ImmutableArray.CreateBuilder<ClusterConfig>(clusters.Count);
        var clusterPlans = ImmutableDictionary.CreateBuilder<string, ClusterExecutionPlan>(StringComparer.Ordinal);
        foreach (var cluster in clusters)
        {
            var current = cluster;
            var compiledPlugins = new List<CompiledClusterPlugin>();
            if (clusterBindings.TryGetValue(cluster.ClusterId, out var targetBindings))
            {
                foreach (var binding in targetBindings)
                {
                    foreach (var compiler in _clusterCompilers.Where(x => x.CanCompile(binding.PluginId)))
                    {
                        var compiled = compiler.Compile(binding, current);
                        current = compiled.Cluster;
                        compiledPlugins.Add(compiled);
                    }
                }
            }
            compiledClusters.Add(current);
            clusterPlans[cluster.ClusterId] = new ClusterExecutionPlan(cluster.ClusterId, PluginExecutionPlanOrdering.Order(compiledPlugins));
        }

        return new GatewaySnapshot(
            version,
            DateTimeOffset.UtcNow,
            compiledRoutes.MoveToImmutable(),
            compiledClusters.MoveToImmutable(),
            routeBindings,
            clusterBindings,
            routePlans.ToImmutable(),
            clusterPlans.ToImmutable());
    }

    private static PluginBindingSnapshot ToSnapshot(
        PluginBindingEntity binding,
        IReadOnlyDictionary<string, string> routeUidMap,
        IReadOnlyDictionary<string, string> clusterUidMap)
    {
        var currentScopeId = binding.Scope switch
        {
            PluginBindingScope.Route when !string.IsNullOrWhiteSpace(binding.RouteUid)
                && routeUidMap.TryGetValue(binding.RouteUid, out var routeId) => routeId,
            PluginBindingScope.Cluster when !string.IsNullOrWhiteSpace(binding.ClusterUid)
                && clusterUidMap.TryGetValue(binding.ClusterUid, out var clusterId) => clusterId,
            _ => binding.ScopeId
        };
        return new PluginBindingSnapshot(binding.Id, binding.PluginId, binding.Scope, currentScopeId,
            binding.ConfigJson, binding.SchemaVersion, binding.ConfigVersion, binding.Order);
    }

    private static IReadOnlyList<TCompiler> BuildCompilerPipeline<TCompiler>(
        IEnumerable<TCompiler>? compilers,
        Func<TCompiler, int> getOrder)
        where TCompiler : class
    {
        var registered = compilers?.ToArray() ?? [];
        if (registered.All(compiler => compiler is not NativePluginAdapters))
            registered = [.. registered, (TCompiler)(object)new NativePluginAdapters()];

        return registered
            .Select((compiler, registrationIndex) => (compiler, registrationIndex))
            .OrderBy(item => getOrder(item.compiler))
            .ThenBy(item => item.registrationIndex)
            .Select(item => item.compiler)
            .ToArray();
    }

    private static ImmutableDictionary<string, ImmutableArray<PluginBindingSnapshot>> Group(
        IEnumerable<PluginBindingSnapshot> bindings, PluginBindingScope scope)
        => bindings.Where(x => x.Scope == scope)
            .GroupBy(x => x.ScopeId, StringComparer.Ordinal)
            .ToImmutableDictionary(
                x => x.Key,
                x => x.OrderBy(y => y.Order).ThenBy(y => y.PluginId, StringComparer.Ordinal).ToImmutableArray(),
                StringComparer.Ordinal);
}

/// <summary>Thread-safe in-memory publication point for compiled snapshots.</summary>
public sealed class GatewaySnapshotPublisher : IGatewaySnapshotPublisher
{
    private GatewaySnapshot _current = GatewaySnapshot.Empty;
    public GatewaySnapshot Current => Volatile.Read(ref _current);

    public GatewaySnapshot Publish(GatewaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        GatewaySnapshot observed;
        do
        {
            observed = Current;
            if (snapshot.Version <= observed.Version)
                throw new InvalidOperationException($"Snapshot version {snapshot.Version} must be greater than current version {observed.Version}.");
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _current, snapshot, observed), observed));
        return snapshot;
    }
}
