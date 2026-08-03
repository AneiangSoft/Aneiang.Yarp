using System.Collections.Immutable;
using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Stable ordering stages used while compiling and executing gateway plugins.</summary>
public enum PluginExecutionStage
{
    NativeConfiguration = 0,
    PreProxy = 100,
    RateLimit = 200,
    CacheLookup = 300,
    Retry = 400,
    DestinationSelection = 500,
    Forward = 600,
    PostProxy = 700,
    Telemetry = 800
}

/// <summary>Immutable result produced by a route plugin compiler.</summary>
public sealed record CompiledRoutePlugin(
    PluginBindingSnapshot Binding,
    PluginExecutionStage Stage,
    object RuntimeConfig,
    RouteConfig Route)
{
    public T GetRuntimeConfig<T>() where T : class =>
        RuntimeConfig as T ?? throw new InvalidOperationException(
            $"Plugin '{Binding.PluginId}' runtime config is not {typeof(T).FullName}.");
}

/// <summary>Immutable result produced by a cluster plugin compiler.</summary>
public sealed record CompiledClusterPlugin(
    PluginBindingSnapshot Binding,
    PluginExecutionStage Stage,
    object RuntimeConfig,
    ClusterConfig Cluster)
{
    public T GetRuntimeConfig<T>() where T : class =>
        RuntimeConfig as T ?? throw new InvalidOperationException(
            $"Plugin '{Binding.PluginId}' runtime config is not {typeof(T).FullName}.");
}

/// <summary>Compiles route binding JSON once, during snapshot publication.</summary>
public interface IRoutePluginCompiler
{
    /// <summary>Controls compiler pipeline order when multiple compilers handle the same binding.</summary>
    int Order => 0;
    bool CanCompile(string pluginId);
    CompiledRoutePlugin Compile(PluginBindingSnapshot binding, RouteConfig route);
}

/// <summary>Compiles cluster binding JSON once, during snapshot publication.</summary>
public interface IClusterPluginCompiler
{
    /// <summary>Controls compiler pipeline order when multiple compilers handle the same binding.</summary>
    int Order => 0;
    bool CanCompile(string pluginId);
    CompiledClusterPlugin Compile(PluginBindingSnapshot binding, ClusterConfig cluster);
}

/// <summary>Immutable, deterministically ordered execution plan for one route.</summary>
public sealed record RouteExecutionPlan(
    string RouteId,
    ImmutableArray<CompiledRoutePlugin> Plugins)
{
    public static RouteExecutionPlan Empty(string routeId) => new(routeId, []);

    public IEnumerable<CompiledRoutePlugin> ForStage(PluginExecutionStage stage) =>
        Plugins.Where(x => x.Stage == stage);

    public bool TryGet(string pluginId, out CompiledRoutePlugin? plugin)
    {
        plugin = Plugins.FirstOrDefault(x => string.Equals(x.Binding.PluginId, pluginId, StringComparison.Ordinal));
        return plugin is not null;
    }
}

/// <summary>Immutable, deterministically ordered execution plan for one cluster.</summary>
public sealed record ClusterExecutionPlan(
    string ClusterId,
    ImmutableArray<CompiledClusterPlugin> Plugins)
{
    public static ClusterExecutionPlan Empty(string clusterId) => new(clusterId, []);

    public IEnumerable<CompiledClusterPlugin> ForStage(PluginExecutionStage stage) =>
        Plugins.Where(x => x.Stage == stage);

    public bool TryGet(string pluginId, out CompiledClusterPlugin? plugin)
    {
        plugin = Plugins.FirstOrDefault(x => string.Equals(x.Binding.PluginId, pluginId, StringComparison.Ordinal));
        return plugin is not null;
    }
}

internal static class PluginExecutionPlanOrdering
{
    public static ImmutableArray<CompiledRoutePlugin> Order(IEnumerable<CompiledRoutePlugin> plugins) =>
        plugins.OrderBy(x => x.Stage)
            .ThenBy(x => x.Binding.Order)
            .ThenBy(x => x.Binding.PluginId, StringComparer.Ordinal)
            .ThenBy(x => x.Binding.BindingId, StringComparer.Ordinal)
            .ToImmutableArray();

    public static ImmutableArray<CompiledClusterPlugin> Order(IEnumerable<CompiledClusterPlugin> plugins) =>
        plugins.OrderBy(x => x.Stage)
            .ThenBy(x => x.Binding.Order)
            .ThenBy(x => x.Binding.PluginId, StringComparer.Ordinal)
            .ThenBy(x => x.Binding.BindingId, StringComparer.Ordinal)
            .ToImmutableArray();
}
