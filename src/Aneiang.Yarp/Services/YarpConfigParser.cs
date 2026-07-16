using Microsoft.Extensions.Configuration;

namespace Aneiang.Yarp.Services;

internal static partial class YarpConfigParser
{
    private static Dictionary<string, string>? ParseMetadata(IConfigurationSection section)
    {
        if (!section.Exists()) return null;
        var dict = new Dictionary<string, string>();
        foreach (var child in section.GetChildren())
            if (child.Value != null) dict[child.Key] = child.Value;
        return dict.Count > 0 ? dict : null;
    }
}
