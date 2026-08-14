using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Route CORS adapter: compiles to YARP native CorsPolicy field.</summary>
public static class CorsAdapter
{
    public const string PluginId = "native.route.cors";

    public static NativePluginAdapterDescriptor Descriptor { get; } = new(PluginId, "Route CORS", PluginBindingScope.Route);

    public static RouteConfig Apply(RouteConfig route, RouteCorsConfig value) =>
        route with { CorsPolicy = NativeAdapterHelpers.Required(value.CorsPolicy, "CorsPolicy") };
}

/// <summary>Configuration model for <see cref="CorsAdapter"/>.</summary>
public sealed class RouteCorsConfig
{
    public string? CorsPolicy { get; init; }
}
