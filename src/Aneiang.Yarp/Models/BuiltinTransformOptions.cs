using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Aneiang.Yarp.Middleware;

/// <summary>
/// Options for built-in request/response transforms.
/// Bound from <c>Gateway:Transforms</c> config section.
/// </summary>
public class BuiltinTransformOptions
{
    public const string SectionName = "Gateway:Transforms";

    /// <summary>Add X-Request-Id header to all proxy requests if not present. Default: true.</summary>
    public bool EnableRequestIdHeader { get; set; } = true;

    /// <summary>Add X-Forwarded-For header with client IP. Default: true.</summary>
    public bool EnableForwardedForHeader { get; set; } = true;

    /// <summary>Remove Server response header from proxied responses. Default: false.</summary>
    public bool RemoveServerHeader { get; set; }

    /// <summary>Remove X-Powered-By response header from proxied responses. Default: false.</summary>
    public bool RemovePoweredByHeader { get; set; }

    /// <summary>Additional response headers to add to all proxy responses.</summary>
    public Dictionary<string, string>? AddResponseHeaders { get; set; }
}
