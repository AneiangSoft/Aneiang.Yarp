using Microsoft.Extensions.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Static helper for parsing YARP routes, clusters, and transforms from IConfiguration.</summary>
internal static partial class YarpConfigParser
{
    /// <summary>Parse key-value metadata from configuration section.</summary>
    /// <remarks>Shared by route and cluster parsing.</remarks>
    private static Dictionary<string, string>? ParseMetadata(IConfigurationSection section)
    {
        if (!section.Exists()) return null;
        var dict = new Dictionary<string, string>();
        foreach (var child in section.GetChildren())
            if (child.Value != null) dict[child.Key] = child.Value;
        return dict.Count > 0 ? dict : null;
    }
}
