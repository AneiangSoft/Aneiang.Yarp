using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Route transforms adapter: compiles to YARP native Transforms field.</summary>
public static class TransformsAdapter
{
    public const string PluginId = "native.route.transforms";

    public static NativePluginAdapterDescriptor Descriptor { get; } = new(PluginId, "Route Transforms", PluginBindingScope.Route);

    public static RouteConfig Apply(RouteConfig route, RouteTransformsConfig value) =>
        route with { Transforms = ValidateTransforms(value.Transforms) };

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ValidateTransforms(List<Dictionary<string, string>>? transforms)
    {
        if (transforms == null || transforms.Count == 0 || transforms.Any(x => x.Count == 0))
            throw new ArgumentException("Transforms must contain at least one non-empty transform object.");
        return transforms.Select(x => (IReadOnlyDictionary<string, string>)x).ToArray();
    }
}

/// <summary>Configuration model for <see cref="TransformsAdapter"/>.</summary>
public sealed class RouteTransformsConfig
{
    public List<Dictionary<string, string>>? Transforms { get; init; }
}
