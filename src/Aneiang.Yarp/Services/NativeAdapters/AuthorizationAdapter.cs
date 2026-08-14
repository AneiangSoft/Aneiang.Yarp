using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Route authorization adapter: compiles to YARP native AuthorizationPolicy field.</summary>
public static class AuthorizationAdapter
{
    public const string PluginId = "native.route.authorization";

    public static NativePluginAdapterDescriptor Descriptor { get; } = new(PluginId, "Route Authorization", PluginBindingScope.Route);

    public static RouteConfig Apply(RouteConfig route, RouteAuthorizationConfig value) =>
        route with { AuthorizationPolicy = NativeAdapterHelpers.Required(value.AuthorizationPolicy, "AuthorizationPolicy") };
}

/// <summary>Configuration model for <see cref="AuthorizationAdapter"/>.</summary>
public sealed class RouteAuthorizationConfig
{
    public string? AuthorizationPolicy { get; init; }
}
