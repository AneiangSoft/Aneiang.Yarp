using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Route timeout adapter: compiles to YARP native Timeout/TimeoutPolicy fields.</summary>
public static class TimeoutAdapter
{
    public const string PluginId = "native.route.timeout";

    public static NativePluginAdapterDescriptor Descriptor { get; } = new(PluginId, "Route Timeout", PluginBindingScope.Route);

    public static RouteConfig Apply(RouteConfig route, RouteTimeoutConfig value)
    {
        if (value.Timeout.HasValue && !string.IsNullOrWhiteSpace(value.TimeoutPolicy))
            throw new ArgumentException("Timeout and TimeoutPolicy are mutually exclusive.");
        if (!value.Timeout.HasValue && string.IsNullOrWhiteSpace(value.TimeoutPolicy))
            throw new ArgumentException("Timeout or TimeoutPolicy is required.");
        if (value.Timeout <= TimeSpan.Zero)
            throw new ArgumentException("Timeout must be greater than zero.");
        return route with { Timeout = value.Timeout, TimeoutPolicy = value.TimeoutPolicy };
    }
}

/// <summary>Configuration model for <see cref="TimeoutAdapter"/>.</summary>
public sealed class RouteTimeoutConfig
{
    public TimeSpan? Timeout { get; init; }
    public string? TimeoutPolicy { get; init; }
}
