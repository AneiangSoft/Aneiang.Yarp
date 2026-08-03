using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aneiang.Yarp.Storage.Entities;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace Aneiang.Yarp.Services;

/// <summary>Built-in plugins that compile configuration directly to YARP native fields.</summary>
public sealed class NativePluginAdapters : IRoutePluginCompiler, IClusterPluginCompiler
{
    public const string RouteTimeout = "native.route.timeout";
    public const string RouteAuthorization = "native.route.authorization";
    public const string RouteTransforms = "native.route.transforms";
    public const string RouteCors = "native.route.cors";
    public const string RouteRateLimit = "native.route.rate-limit";
    public const string RouteCompression = "native.route.compression";
    public const string ClusterLoadBalancing = "native.cluster.load-balancing";
    public const string ClusterHealthCheck = "native.cluster.health-check";
    public const string ClusterSessionAffinity = "native.cluster.session-affinity";
    public const string ClusterHttpClient = "native.cluster.http-client";
    public const string ClusterHttpRequest = "native.cluster.http-request";

    public static IReadOnlyList<NativePluginAdapterDescriptor> Catalog { get; } =
    [
        new(RouteTimeout, "Route Timeout", PluginBindingScope.Route),
        new(RouteAuthorization, "Route Authorization", PluginBindingScope.Route),
        new(RouteTransforms, "Route Transforms", PluginBindingScope.Route),
        new(RouteCors, "Route CORS", PluginBindingScope.Route),
        new(RouteRateLimit, "Route Rate Limit", PluginBindingScope.Route),
        new(RouteCompression, "Route Compression", PluginBindingScope.Route),
        new(ClusterLoadBalancing, "Cluster Load Balancing", PluginBindingScope.Cluster),
        new(ClusterHealthCheck, "Cluster Health Check", PluginBindingScope.Cluster),
        new(ClusterSessionAffinity, "Cluster Session Affinity", PluginBindingScope.Cluster),
        new(ClusterHttpClient, "Cluster HTTP Client", PluginBindingScope.Cluster),
        new(ClusterHttpRequest, "Cluster HTTP Request", PluginBindingScope.Cluster)
    ];

    private static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    static NativePluginAdapters()
    {
        StrictJsonOptions.Converters.Add(new JsonStringEnumConverter());
        StrictJsonOptions.Converters.Add(new StrictVersionConverter());
    }

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
            RouteTimeout => ApplyTimeout(route, (RouteTimeoutConfig)(runtimeConfig = Deserialize<RouteTimeoutConfig>(binding.ConfigJson))),
            RouteAuthorization => route with { AuthorizationPolicy = Required(((RouteAuthorizationConfig)(runtimeConfig = Deserialize<RouteAuthorizationConfig>(binding.ConfigJson))).AuthorizationPolicy, "AuthorizationPolicy") },
            RouteTransforms => route with { Transforms = ValidateTransforms(((RouteTransformsConfig)(runtimeConfig = Deserialize<RouteTransformsConfig>(binding.ConfigJson))).Transforms) },
            RouteCors => route with { CorsPolicy = Required(((RouteCorsConfig)(runtimeConfig = Deserialize<RouteCorsConfig>(binding.ConfigJson))).CorsPolicy, "CorsPolicy") },
            RouteRateLimit => route with { RateLimiterPolicy = Required(((RouteRateLimitConfig)(runtimeConfig = Deserialize<RouteRateLimitConfig>(binding.ConfigJson))).RateLimiterPolicy, "RateLimiterPolicy") },
            RouteCompression => ApplyCompression(route, (RouteCompressionConfig)(runtimeConfig = Deserialize<RouteCompressionConfig>(binding.ConfigJson))),
            _ => throw new ArgumentException($"Unknown native route adapter '{binding.PluginId}'.")
        };
        return new CompiledRoutePlugin(binding, PluginExecutionStage.NativeConfiguration, runtimeConfig, compiledRoute);
    }

    public static CompiledClusterPlugin CompileCluster(PluginBindingSnapshot binding, ClusterConfig cluster)
    {
        object runtimeConfig;
        var compiledCluster = binding.PluginId switch
        {
            ClusterLoadBalancing => cluster with { LoadBalancingPolicy = Required(((ClusterLoadBalancingConfig)(runtimeConfig = Deserialize<ClusterLoadBalancingConfig>(binding.ConfigJson))).LoadBalancingPolicy, "LoadBalancingPolicy") },
            ClusterHealthCheck => cluster with { HealthCheck = ToHealthCheck((ClusterHealthCheckConfig)(runtimeConfig = Deserialize<ClusterHealthCheckConfig>(binding.ConfigJson))) },
            ClusterSessionAffinity => cluster with { SessionAffinity = ToSessionAffinity((ClusterSessionAffinityConfig)(runtimeConfig = Deserialize<ClusterSessionAffinityConfig>(binding.ConfigJson))) },
            ClusterHttpClient => cluster with { HttpClient = ToHttpClient((ClusterHttpClientConfig)(runtimeConfig = Deserialize<ClusterHttpClientConfig>(binding.ConfigJson))) },
            ClusterHttpRequest => cluster with { HttpRequest = ToHttpRequest((ClusterHttpRequestConfig)(runtimeConfig = Deserialize<ClusterHttpRequestConfig>(binding.ConfigJson))) },
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
                ApplyTimeout(new RouteConfig(), Deserialize<RouteTimeoutConfig>(json));
                break;
            case RouteAuthorization:
                Required(Deserialize<RouteAuthorizationConfig>(json).AuthorizationPolicy, "AuthorizationPolicy");
                break;
            case RouteTransforms:
                ValidateTransforms(Deserialize<RouteTransformsConfig>(json).Transforms);
                break;
            case RouteCors:
                Required(Deserialize<RouteCorsConfig>(json).CorsPolicy, "CorsPolicy");
                break;
            case RouteRateLimit:
                Required(Deserialize<RouteRateLimitConfig>(json).RateLimiterPolicy, "RateLimiterPolicy");
                break;
            case RouteCompression:
                ApplyCompression(new RouteConfig(), Deserialize<RouteCompressionConfig>(json));
                break;
            case ClusterLoadBalancing:
                Required(Deserialize<ClusterLoadBalancingConfig>(json).LoadBalancingPolicy, "LoadBalancingPolicy");
                break;
            case ClusterHealthCheck:
                ToHealthCheck(Deserialize<ClusterHealthCheckConfig>(json));
                break;
            case ClusterSessionAffinity:
                ToSessionAffinity(Deserialize<ClusterSessionAffinityConfig>(json));
                break;
            case ClusterHttpClient:
                ToHttpClient(Deserialize<ClusterHttpClientConfig>(json));
                break;
            case ClusterHttpRequest:
                ToHttpRequest(Deserialize<ClusterHttpRequestConfig>(json));
                break;
            default:
                throw new ArgumentException($"Unknown native adapter '{pluginId}'.");
        }
    }

    private static T Deserialize<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("ConfigJson is required.");
        return JsonSerializer.Deserialize<T>(json, StrictJsonOptions)
            ?? throw new ArgumentException("ConfigJson must contain a JSON object.");
    }

    private static RouteConfig ApplyTimeout(RouteConfig route, RouteTimeoutConfig value)
    {
        if (value.Timeout.HasValue && !string.IsNullOrWhiteSpace(value.TimeoutPolicy))
            throw new ArgumentException("Timeout and TimeoutPolicy are mutually exclusive.");
        if (!value.Timeout.HasValue && string.IsNullOrWhiteSpace(value.TimeoutPolicy))
            throw new ArgumentException("Timeout or TimeoutPolicy is required.");
        if (value.Timeout <= TimeSpan.Zero)
            throw new ArgumentException("Timeout must be greater than zero.");
        return route with { Timeout = value.Timeout, TimeoutPolicy = value.TimeoutPolicy };
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ValidateTransforms(List<Dictionary<string, string>>? transforms)
    {
        if (transforms == null || transforms.Count == 0 || transforms.Any(x => x.Count == 0))
            throw new ArgumentException("Transforms must contain at least one non-empty transform object.");
        return transforms.Select(x => (IReadOnlyDictionary<string, string>)x).ToArray();
    }

    private static RouteConfig ApplyCompression(RouteConfig route, RouteCompressionConfig value)
    {
        if (value.Enabled)
            return route;

        var transforms = route.Transforms?.Select(transform =>
                (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(transform, StringComparer.OrdinalIgnoreCase))
            .ToList() ?? [];
        if (!transforms.Any(transform =>
                transform.TryGetValue("RequestHeaderRemove", out var header) &&
                string.Equals(header, "Accept-Encoding", StringComparison.OrdinalIgnoreCase)))
        {
            transforms.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["RequestHeaderRemove"] = "Accept-Encoding"
            });
        }

        return route with { Transforms = transforms };
    }

    private static HealthCheckConfig ToHealthCheck(ClusterHealthCheckConfig value)
    {
        if (value.Active == null && value.Passive == null && string.IsNullOrWhiteSpace(value.AvailableDestinationsPolicy))
            throw new ArgumentException("At least one health-check setting is required.");
        if (value.Active?.Interval <= TimeSpan.Zero || value.Active?.Timeout <= TimeSpan.Zero || value.Passive?.ReactivationPeriod <= TimeSpan.Zero)
            throw new ArgumentException("Health-check durations must be greater than zero.");
        return new HealthCheckConfig
        {
            Active = value.Active == null ? null : new ActiveHealthCheckConfig
            {
                Enabled = value.Active.Enabled,
                Interval = value.Active.Interval,
                Timeout = value.Active.Timeout,
                Policy = value.Active.Policy,
                Path = value.Active.Path,
                Query = value.Active.Query
            },
            Passive = value.Passive == null ? null : new PassiveHealthCheckConfig
            {
                Enabled = value.Passive.Enabled,
                Policy = value.Passive.Policy,
                ReactivationPeriod = value.Passive.ReactivationPeriod
            },
            AvailableDestinationsPolicy = value.AvailableDestinationsPolicy
        };
    }

    private static SessionAffinityConfig ToSessionAffinity(ClusterSessionAffinityConfig value)
    {
        if (value.Enabled && string.IsNullOrWhiteSpace(value.Policy))
            throw new ArgumentException("Policy is required when session affinity is enabled.");
        return new SessionAffinityConfig
        {
            Enabled = value.Enabled,
            Policy = value.Policy,
            FailurePolicy = value.FailurePolicy,
            AffinityKeyName = Required(value.AffinityKeyName, "AffinityKeyName")
        };
    }

    private static HttpClientConfig ToHttpClient(ClusterHttpClientConfig value)
    {
        if (value.MaxConnectionsPerServer <= 0)
            throw new ArgumentException("MaxConnectionsPerServer must be greater than zero.");
        return new HttpClientConfig
        {
            SslProtocols = value.SslProtocols,
            DangerousAcceptAnyServerCertificate = value.DangerousAcceptAnyServerCertificate,
            MaxConnectionsPerServer = value.MaxConnectionsPerServer,
            EnableMultipleHttp2Connections = value.EnableMultipleHttp2Connections,
            RequestHeaderEncoding = value.RequestHeaderEncoding,
            ResponseHeaderEncoding = value.ResponseHeaderEncoding,
            WebProxy = value.WebProxy == null ? null : new WebProxyConfig
            {
                Address = value.WebProxy.Address,
                BypassOnLocal = value.WebProxy.BypassOnLocal,
                UseDefaultCredentials = value.WebProxy.UseDefaultCredentials
            }
        };
    }

    private static ForwarderRequestConfig ToHttpRequest(ClusterHttpRequestConfig value)
    {
        if (value.ActivityTimeout <= TimeSpan.Zero)
            throw new ArgumentException("ActivityTimeout must be greater than zero.");
        return new ForwarderRequestConfig
        {
            ActivityTimeout = value.ActivityTimeout,
            Version = value.Version,
            VersionPolicy = value.VersionPolicy,
            AllowResponseBuffering = value.AllowResponseBuffering
        };
    }

    private static string Required(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"{name} is required.");

    private sealed class StrictVersionConverter : JsonConverter<Version>
    {
        public override Version? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (!Version.TryParse(value, out var version)) throw new JsonException($"Invalid HTTP version '{value}'.");
            return version;
        }

        public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}

public sealed record NativePluginAdapterDescriptor(string PluginId, string DisplayName, PluginBindingScope Scope);

public sealed class RouteTimeoutConfig
{
    public TimeSpan? Timeout { get; init; }
    public string? TimeoutPolicy { get; init; }
}

public sealed class RouteAuthorizationConfig
{
    public string? AuthorizationPolicy { get; init; }
}

public sealed class RouteTransformsConfig
{
    public List<Dictionary<string, string>>? Transforms { get; init; }
}

public sealed class RouteCorsConfig
{
    public string? CorsPolicy { get; init; }
}

public sealed class RouteRateLimitConfig
{
    public string? RateLimiterPolicy { get; init; }
}

public sealed class RouteCompressionConfig
{
    public bool Enabled { get; init; } = true;
}

public sealed class ClusterLoadBalancingConfig
{
    public string? LoadBalancingPolicy { get; init; }
}

public sealed class ClusterHealthCheckConfig
{
    public NativeActiveHealthCheckConfig? Active { get; init; }
    public NativePassiveHealthCheckConfig? Passive { get; init; }
    public string? AvailableDestinationsPolicy { get; init; }
}

public sealed class NativeActiveHealthCheckConfig
{
    public bool Enabled { get; init; }
    public TimeSpan? Interval { get; init; }
    public TimeSpan? Timeout { get; init; }
    public string? Policy { get; init; }
    public string? Path { get; init; }
    public string? Query { get; init; }
}

public sealed class NativePassiveHealthCheckConfig
{
    public bool Enabled { get; init; }
    public string? Policy { get; init; }
    public TimeSpan? ReactivationPeriod { get; init; }
}

public sealed class ClusterSessionAffinityConfig
{
    public bool Enabled { get; init; }
    public string? Policy { get; init; }
    public string? FailurePolicy { get; init; }
    public string? AffinityKeyName { get; init; }
}

public sealed class ClusterHttpClientConfig
{
    public SslProtocols? SslProtocols { get; init; }
    public bool? DangerousAcceptAnyServerCertificate { get; init; }
    public int? MaxConnectionsPerServer { get; init; }
    public bool? EnableMultipleHttp2Connections { get; init; }
    public string? RequestHeaderEncoding { get; init; }
    public string? ResponseHeaderEncoding { get; init; }
    public NativeWebProxyConfig? WebProxy { get; init; }
}

public sealed class NativeWebProxyConfig
{
    public Uri? Address { get; init; }
    public bool? BypassOnLocal { get; init; }
    public bool? UseDefaultCredentials { get; init; }
}

public sealed class ClusterHttpRequestConfig
{
    public TimeSpan? ActivityTimeout { get; init; }
    public Version? Version { get; init; }
    public HttpVersionPolicy? VersionPolicy { get; init; }
    public bool? AllowResponseBuffering { get; init; }
}
