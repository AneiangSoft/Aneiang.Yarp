using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Route compression adapter: disables response compression by stripping the Accept-Encoding request header.</summary>
public static class CompressionAdapter
{
    public const string PluginId = "native.route.compression";

    public static NativePluginAdapterDescriptor Descriptor { get; } = new(PluginId, "Route Compression", PluginBindingScope.Route);

    public static RouteConfig Apply(RouteConfig route, RouteCompressionConfig value)
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
}

/// <summary>Configuration model for <see cref="CompressionAdapter"/>.</summary>
public sealed class RouteCompressionConfig
{
    public bool Enabled { get; init; } = true;
}
