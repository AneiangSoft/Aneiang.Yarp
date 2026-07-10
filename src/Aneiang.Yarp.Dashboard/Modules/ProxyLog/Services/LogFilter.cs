using Aneiang.Yarp.Dashboard.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Default log filter implementation. Pre-caches configuration values for fast O(1) checks.
/// </summary>
public sealed class LogFilter : ILogFilter
{
    private readonly string _dashPrefix;
    private readonly bool _loggingEnabled;
    private readonly int _minLogLevelNumeric;
    private readonly bool _logErrorsOnly;
    private readonly HashSet<string>? _routeWhitelist;
    private readonly HashSet<string>? _routeBlacklist;
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

    public LogFilter(IOptions<DashboardOptions> options, ILogSampler sampler)
    {
        var opt = options.Value;
        _dashPrefix = "/" + opt.RoutePrefix.Trim('/');
        _loggingEnabled = opt.EnableProxyLogging;
        _logErrorsOnly = opt.LogErrorsOnly;
        _sampler = sampler;

        _minLogLevelNumeric = opt.MinLogLevel switch
        {
            "Critical" => 4,
            "Error" => 3,
            "Warning" => 2,
            "Information" => 1,
            _ => 0
        };

        if (opt.LogRouteWhitelist?.Count > 0)
            _routeWhitelist = new HashSet<string>(opt.LogRouteWhitelist, StringComparer.OrdinalIgnoreCase);
        if (opt.LogRouteBlacklist?.Count > 0)
            _routeBlacklist = new HashSet<string>(opt.LogRouteBlacklist, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether proxy logging is enabled at all.</summary>
    public bool IsLoggingEnabled => _loggingEnabled;

    public bool IsSkippedRequest(HttpContext context)
    {
        var path = context.Request.Path;

        // Skip Dashboard requests
        if (path.StartsWithSegments(_dashPrefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments(ContentRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        // Skip gRPC — response body capture breaks HTTP/2 trailer support
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
        // Min log level check
        var currentLevel = context.Response.StatusCode switch
        {
            >= 500 => 3,
            >= 400 => 2,
            _ => 1
        };
        if (currentLevel < _minLogLevelNumeric)
            return false;

        // Errors-only mode
        if (_logErrorsOnly && context.Response.StatusCode < 400)
            return false;

        // Route whitelist
        if (_routeWhitelist != null &&
            (string.IsNullOrEmpty(routeId) || !_routeWhitelist.Contains(routeId)))
            return false;

        // Route blacklist
        if (_routeBlacklist != null &&
            !string.IsNullOrEmpty(routeId) && _routeBlacklist.Contains(routeId))
            return false;

        // Sampling
        if (!_sampler.ShouldSample())
            return false;

        return true;
    }
}
