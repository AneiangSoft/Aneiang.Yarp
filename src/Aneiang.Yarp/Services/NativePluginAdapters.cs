using System.Text.Json;
using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Built-in plugins that compile configuration directly to YARP native fields.
/// Acts as the aggregator for the adapters in <c>Services/NativeAdapters</c>: each adapter owns its
/// plugin id, descriptor, validation and compile logic; this class exposes the shared catalog and API.
/// </summary>
public sealed class NativePluginAdapters : IRoutePluginCompiler, IClusterPluginCompiler
{
    public const string RouteTimeout = TimeoutAdapter.PluginId;
    public const string RouteAuthorization = AuthorizationAdapter.PluginId;
    public const string RouteTransforms = TransformsAdapter.PluginId;
    public const string RouteCors = CorsAdapter.PluginId;
    public const string RouteCompression = CompressionAdapter.PluginId;
    public const string ClusterLoadBalancing = LoadBalancingAdapter.PluginId;
    public const string ClusterHealthCheck = HealthCheckAdapter.PluginId;
    public const string ClusterSessionAffinity = SessionAffinityAdapter.PluginId;
    public const string ClusterHttpClient = HttpClientAdapter.PluginId;
    public const string ClusterHttpRequest = HttpRequestAdapter.PluginId;

    public static IReadOnlyList<NativePluginAdapterDescriptor> Catalog { get; } =
    [
        TimeoutAdapter.Descriptor,
        AuthorizationAdapter.Descriptor,
        TransformsAdapter.Descriptor,
        CorsAdapter.Descriptor,
        CompressionAdapter.Descriptor,
        LoadBalancingAdapter.Descriptor,
        HealthCheckAdapter.Descriptor,
        SessionAffinityAdapter.Descriptor,
        HttpClientAdapter.Descriptor,
        HttpRequestAdapter.Descriptor
    ];

    public static bool IsNative(string pluginId) => Catalog.Any(x => x.PluginId == pluginId);

    public static bool TryValidate(
        string pluginId,
        PluginBindingScope scope,
        string configJson,
        out string? error)
    {
        var descriptor = Catalog.FirstOrDefault(x => x.PluginId == pluginId);
        if (descriptor == null)
        {
            error = $"Unknown native adapter '{pluginId}'.";
            return false;
        }

        if (descriptor.Scope != scope)
        {
            error = $"Native adapter '{pluginId}' can only be bound to {descriptor.Scope}.";
            return false;
        }

        try
        {
            ValidateModel(pluginId, configJson);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    bool IRoutePluginCompiler.CanCompile(string pluginId) => IsNativeRoute(pluginId);

    CompiledRoutePlugin IRoutePluginCompiler.Compile(PluginBindingSnapshot binding, RouteConfig route) =>
        CompileRoute(binding, route);

    bool IClusterPluginCompiler.CanCompile(string pluginId) => IsNativeCluster(pluginId);

    CompiledClusterPlugin IClusterPluginCompiler.Compile(PluginBindingSnapshot binding, ClusterConfig cluster) =>
        CompileCluster(binding, cluster);

    public static bool IsNativeRoute(string pluginId) =>
        Catalog.Any(x => x.Scope == PluginBindingScope.Route && x.PluginId == pluginId);

    public static bool IsNativeCluster(string pluginId) =>
        Catalog.Any(x => x.Scope == PluginBindingScope.Cluster && x.PluginId == pluginId);

    public static CompiledRoutePlugin CompileRoute(PluginBindingSnapshot binding, RouteConfig route)
    {
        object runtimeConfig;
        var compiledRoute = binding.PluginId switch
        {
            RouteTimeout => TimeoutAdapter.Apply(route, (RouteTimeoutConfig)(runtimeConfig = NativeAdapterHelpers.Deserialize<RouteTimeoutConfig>(binding.ConfigJson))),
            RouteAuthorization => AuthorizationAdapter.Apply(route, (RouteAuthorizationConfig)(runtimeConfig = NativeAdapterHelpers.Deserialize<RouteAuthorizationConfig>(binding.ConfigJson))),
            RouteTransforms => TransformsAdapter.Apply(route, (RouteTransformsConfig)(runtimeConfig = NativeAdapterHelpers.Deserialize<RouteTransformsConfig>(binding.ConfigJson))),
            RouteCors => CorsAdapter.Apply(route, (RouteCorsConfig)(runtimeConfig = NativeAdapterHelpers.Deserialize<RouteCorsConfig>(binding.ConfigJson))),
            RouteCompression => CompressionAdapter.Apply(route, (RouteCompressionConfig)(runtimeConfig = NativeAdapterHelpers.Deserialize<RouteCompressionConfig>(binding.ConfigJson))),
            _ => throw new ArgumentException($"Unknown native route adapter '{binding.PluginId}'.")
        };
        return new CompiledRoutePlugin(binding, PluginExecutionStage.NativeConfiguration, runtimeConfig, compiledRoute);
    }

    public static CompiledClusterPlugin CompileCluster(PluginBindingSnapshot binding, ClusterConfig cluster)
    {
        object runtimeConfig;
        var compiledCluster = binding.PluginId switch
        {
            ClusterLoadBalancing => LoadBalancingAdapter.Apply(cluster, (ClusterLoadBalancingConfig)(runtimeConfig = NativeAdapterHelpers.Deserialize<ClusterLoadBalancingConfig>(binding.ConfigJson))),
            ClusterHealthCheck => HealthCheckAdapter.Apply(cluster, (ClusterHealthCheckConfig)(runtimeConfig = NativeAdapterHelpers.Deserialize<ClusterHealthCheckConfig>(binding.ConfigJson))),
            ClusterSessionAffinity => SessionAffinityAdapter.Apply(cluster, (ClusterSessionAffinityConfig)(runtimeConfig = NativeAdapterHelpers.Deserialize<ClusterSessionAffinityConfig>(binding.ConfigJson))),
            ClusterHttpClient => HttpClientAdapter.Apply(cluster, (ClusterHttpClientConfig)(runtimeConfig = NativeAdapterHelpers.Deserialize<ClusterHttpClientConfig>(binding.ConfigJson))),
            ClusterHttpRequest => HttpRequestAdapter.Apply(cluster, (ClusterHttpRequestConfig)(runtimeConfig = NativeAdapterHelpers.Deserialize<ClusterHttpRequestConfig>(binding.ConfigJson))),
            _ => throw new ArgumentException($"Unknown native cluster adapter '{binding.PluginId}'.")
        };
        return new CompiledClusterPlugin(binding, PluginExecutionStage.NativeConfiguration, runtimeConfig, compiledCluster);
    }

    public static RouteConfig ApplyRoute(RouteConfig route, IEnumerable<PluginBindingSnapshot> bindings)
    {
        foreach (var binding in bindings.OrderBy(x => x.Order).ThenBy(x => x.PluginId, StringComparer.Ordinal))
            route = CompileRoute(binding, route).Route;
        return route;
    }

    public static ClusterConfig ApplyCluster(ClusterConfig cluster, IEnumerable<PluginBindingSnapshot> bindings)
    {
        foreach (var binding in bindings.OrderBy(x => x.Order).ThenBy(x => x.PluginId, StringComparer.Ordinal))
            cluster = CompileCluster(binding, cluster).Cluster;
        return cluster;
    }

    private static void ValidateModel(string pluginId, string json)
    {
        switch (pluginId)
        {
            case RouteTimeout:
                TimeoutAdapter.Apply(new RouteConfig(), NativeAdapterHelpers.Deserialize<RouteTimeoutConfig>(json));
                break;
            case RouteAuthorization:
                AuthorizationAdapter.Apply(new RouteConfig(), NativeAdapterHelpers.Deserialize<RouteAuthorizationConfig>(json));
                break;
            case RouteTransforms:
                TransformsAdapter.Apply(new RouteConfig(), NativeAdapterHelpers.Deserialize<RouteTransformsConfig>(json));
                break;
            case RouteCors:
                CorsAdapter.Apply(new RouteConfig(), NativeAdapterHelpers.Deserialize<RouteCorsConfig>(json));
                break;
            case RouteCompression:
                CompressionAdapter.Apply(new RouteConfig(), NativeAdapterHelpers.Deserialize<RouteCompressionConfig>(json));
                break;
            case ClusterLoadBalancing:
                LoadBalancingAdapter.Apply(new ClusterConfig(), NativeAdapterHelpers.Deserialize<ClusterLoadBalancingConfig>(json));
                break;
            case ClusterHealthCheck:
                HealthCheckAdapter.Apply(new ClusterConfig(), NativeAdapterHelpers.Deserialize<ClusterHealthCheckConfig>(json));
                break;
            case ClusterSessionAffinity:
                SessionAffinityAdapter.Apply(new ClusterConfig(), NativeAdapterHelpers.Deserialize<ClusterSessionAffinityConfig>(json));
                break;
            case ClusterHttpClient:
                HttpClientAdapter.Apply(new ClusterConfig(), NativeAdapterHelpers.Deserialize<ClusterHttpClientConfig>(json));
                break;
            case ClusterHttpRequest:
                HttpRequestAdapter.Apply(new ClusterConfig(), NativeAdapterHelpers.Deserialize<ClusterHttpRequestConfig>(json));
                break;
            default:
                throw new ArgumentException($"Unknown native adapter '{pluginId}'.");
        }
    }
}
