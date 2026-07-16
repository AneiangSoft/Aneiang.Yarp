namespace Aneiang.Yarp.Models;

public class BuiltinTransformOptions
{
    public const string SectionName = "Gateway:Transforms";

    public bool EnableRequestIdHeader { get; set; } = true;

    public bool EnableForwardedForHeader { get; set; } = true;

    public bool RemoveServerHeader { get; set; }

    public bool RemovePoweredByHeader { get; set; }

    public Dictionary<string, string>? AddResponseHeaders { get; set; }
}
