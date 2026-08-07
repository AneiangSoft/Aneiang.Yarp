using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Plugin.ProxyLog.Services;

/// <summary>
/// Default log filter implementation. Pre-caches configuration values for fast O(1) checks.
/// </summary>
public sealed class LogFilter : ILogFilter
{
    private readonly string _dashPrefix;
    private readonly ProxyLogRuntimeSettings _runtimeSettings;
    private readonly ILogSampler _sampler;

    private const string ContentRoot = "/_content/Aneiang.Yarp.Dashboard";

    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".css", ".map",
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".webp", ".bmp", ".avif",
        ".woff", ".woff2", ".ttf", ".eot", ".otf",
        ".mp3", ".mp4", ".wav", ".avi", ".webm", ".ogg",
        ".pdf", ".zip", ".gz", ".tar", ".rar",
        ".html", ".htm", ".xml", ".txt"
    };

    public LogFilter(
        IOptions<ProxyLogPluginOptions> options,
        ProxyLogRuntimeSettings runtimeSettings,
        ILogSampler sampler)
    {
        _dashPrefix = "/" + options.Value.RoutePrefix.Trim('/');
        _runtimeSettings = runtimeSettings;
        _sampler = sampler;
    }

    public bool IsSkippedRequest(HttpContext context)
    {
        var path = context.Request.Path;

        // Skip Dashboard requests
        if (path.StartsWithSegments(_dashPrefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments(ContentRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        // Skip gRPC - response body capture breaks HTTP/2 trailer support
        if (context.Request.ContentType != null &&
            context.Request.ContentType.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase))
            return true;

        // Skip frontend static resources
        var extension = Path.GetExtension(path.Value);
        if (extension != null && SkippedExtensions.Contains(extension))
            return true;

        return false;
    }

    public bool ShouldLog(HttpContext context, string? routeId)
    {
        var settings = _runtimeSettings.Current;

        // Min log level check
        var currentLevel = context.Response.StatusCode switch
        {
            >= 500 => 3,
            >= 400 => 2,
            _ => 1
        };
        if (currentLevel < settings.MinLogLevelNumeric)
            return false;

        // Errors-only mode
        if (settings.ErrorsOnly && context.Response.StatusCode < 400)
            return false;

        // Sampling
        if (!_sampler.ShouldSample())
            return false;

        return true;
    }
}
