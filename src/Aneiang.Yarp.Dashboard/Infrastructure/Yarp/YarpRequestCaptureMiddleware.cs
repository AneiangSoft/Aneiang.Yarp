using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Yarp;

/// <summary>
/// Captures incoming proxy request/response details before YARP processes the request.
/// Skips Dashboard requests. Buffers body for parameter capture.
/// Supports structured logging, sampling, filtering, and sanitization.
/// Zero dependency on logging frameworks.
/// Optimized with ArrayPool buffer reuse and minimal allocations.
/// </summary>
public sealed class YarpRequestCaptureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IProxyLogStore _store;
    private readonly LogSanitizer _sanitizer;
    private readonly string _dashPrefix;
    private readonly bool _loggingEnabled;
    private readonly bool _enableSampling;
    private readonly double _samplingRate;
    private readonly bool _logErrorsOnly;
    private readonly int _maxBodyLength;

    // Cached collections for fast filtering (avoid property access)
    private readonly HashSet<string>? _routeWhitelist;
    private readonly HashSet<string>? _routeBlacklist;

    // Cached minimum log level for fast filtering
    private int _minLogLevel;
    private bool _minLogLevelChecked;

    // Reusable Random instance per thread to avoid contention
    private static readonly ThreadLocal<Random> _threadRandom = new(() => new Random());

    /// <summary>
    /// Static file extensions to skip from logging (frontend resources).
    /// </summary>
    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".css", ".map",
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".webp", ".bmp", ".avif",
        ".woff", ".woff2", ".ttf", ".eot", ".otf",
        ".mp3", ".mp4", ".wav", ".avi", ".webm", ".ogg",
        ".pdf", ".zip", ".gz", ".tar", ".rar",
        ".html", ".htm", ".xml", ".txt"
    };

    /// <summary>
    /// Content root path for the Dashboard static files. Used to skip logging for frontend resources.
    /// </summary>
    private const string ContentRoot = "/_content/Aneiang.Yarp.Dashboard";


    /// <summary>
    /// Creates the middleware.
    /// Pre-computes and caches all configuration values for optimal performance.
    /// </summary>
    public YarpRequestCaptureMiddleware(
        RequestDelegate next,
        IProxyLogStore store,
        LogSanitizer sanitizer,
        IOptions<DashboardOptions> options)
    {
        _next = next;
        _store = store;
        _sanitizer = sanitizer;

        var opt = options.Value;
        _dashPrefix = "/" + opt.RoutePrefix.Trim('/');
        _loggingEnabled = opt.EnableProxyLogging;

        // Pre-cache all frequently accessed configuration values
        _enableSampling = opt.EnableLogSampling;
        _samplingRate = opt.LogSamplingRate;
        _logErrorsOnly = opt.LogErrorsOnly;
        _maxBodyLength = opt.LogMaxBodyLength;

        // Pre-convert route lists to HashSets for O(1) lookup
        if (opt.LogRouteWhitelist?.Count > 0)
            _routeWhitelist = new HashSet<string>(opt.LogRouteWhitelist, StringComparer.OrdinalIgnoreCase);
        if (opt.LogRouteBlacklist?.Count > 0)
            _routeBlacklist = new HashSet<string>(opt.LogRouteBlacklist, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Captures request/response info. Skips Dashboard paths.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_loggingEnabled)
        {
            await _next(context);
            return;
        }

        // Skip Dashboard requests
        if (context.Request.Path.StartsWithSegments(_dashPrefix, StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments(ContentRoot, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Skip frontend static resource requests
        var extension = Path.GetExtension(context.Request.Path.Value);
        if (extension != null && SkippedExtensions.Contains(extension))
        {
            await _next(context);
            return;
        }

        var timestamp = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();
        // Reuse string from Activity or create minimal allocation Guid
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        // Enable request body buffering for capture
        context.Request.EnableBuffering();

        // Read request body (non-consuming)
        string requestBody = await ReadBodyAsync(context.Request);

        // Capture response body by replacing Response.Body and IHttpResponseBodyFeature.
        // Note: YARP internally manages the response transport stream and may bypass
        // our MemoryStream in proxy scenarios. We capture what we can.
        var originalResponseBody = context.Response.Body;
        var originalBodyFeature = context.Features.Get<IHttpResponseBodyFeature>();
        // Use pooled MemoryStream for reduced GC pressure
        using var responseBodyStream = new MemoryStream(4096); // Pre-allocate 4KB buffer
        var captureFeature = new StreamResponseBodyFeature(responseBodyStream, originalBodyFeature);

        context.Response.Body = responseBodyStream;
        context.Features.Set<IHttpResponseBodyFeature>(captureFeature);

        await _next(context);
        stopwatch.Stop();

        // Get proxy feature once and reuse
        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var downstreamUrl = BuildDownstreamUrl(proxyFeature, context.Request);
        var routeId = proxyFeature?.Route?.Config?.RouteId;
        var clusterId = proxyFeature?.Route?.Config?.ClusterId;

        // Check if should log before expensive body processing operations
        if (!ShouldLog(context, routeId))
        {
            // Still need to restore stream even if not logging
            await RestoreResponseStreamAsync(responseBodyStream, originalResponseBody, originalBodyFeature, context);
            return;
        }

        // Read response body from the captured stream
        string responseBodyText = await ReadStreamAsync(responseBodyStream);

        // Get captured downstream request data (after transforms like encryption)
        var downstreamBody = GetDownstreamBody(context);
        var downstreamMethod = GetDownstreamMethod(context);
        var downstreamUrlCaptured = GetDownstreamUrl(context) ?? downstreamUrl;

        // Restore original response stream and copy back
        await RestoreResponseStreamAsync(responseBodyStream, originalResponseBody, originalBodyFeature, context);

        // Sanitize and truncate request body
        var sanitizedRequestBody = _sanitizer.SanitizeJsonBody(requestBody);
        var requestText = _sanitizer.TruncateText(sanitizedRequestBody, out var requestTruncated);

        // Sanitize and truncate downstream body
        string? downstreamText = null;
        var downstreamTruncatedFlag = false;
        if (downstreamBody != null)
        {
            var sanitizedDownstreamBody = _sanitizer.SanitizeJsonBody(downstreamBody);
            downstreamText = _sanitizer.TruncateText(sanitizedDownstreamBody, out downstreamTruncatedFlag);
        }

        // Build structured request log entry
        _store.Add(new LogEntry
        {
            Timestamp = timestamp,
            EventType = LogEventType.ProxyRequest,
            Level = "Information",
            Category = "Gateway",
            Message = $"[Request] {context.Request.Method} {context.Request.Path}{context.Request.QueryString}",
            TraceId = traceId,
            RouteId = routeId,
            ClusterId = clusterId,
            Method = context.Request.Method,
            UpstreamPath = context.Request.Path + context.Request.QueryString.Value,
            DownstreamUrl = downstreamUrlCaptured,
            DownstreamMethod = downstreamMethod,
            DownstreamBody = downstreamText,
            DownstreamBodyTruncated = downstreamTruncatedFlag,
            RequestHeaders = _sanitizer.SanitizeHeaders(context.Request.Headers),
            RequestBody = requestText,
            RequestBodyTruncated = requestTruncated
        });

        // Sanitize and truncate response body
        var sanitizedResponseBody = _sanitizer.SanitizeJsonBody(responseBodyText);
        var responseText = _sanitizer.TruncateText(sanitizedResponseBody, out var responseTruncated);

        // Build structured response log entry
        _store.Add(new LogEntry
        {
            Timestamp = DateTime.Now,
            EventType = LogEventType.ProxyResponse,
            Level = GetLogLevel(context.Response.StatusCode),
            Category = "Gateway",
            Message = $"[Response] {context.Response.StatusCode} {context.Request.Method} {context.Request.Path}{context.Request.QueryString}",
            TraceId = traceId,
            RouteId = routeId,
            ClusterId = clusterId,
            Method = context.Request.Method,
            UpstreamPath = context.Request.Path + context.Request.QueryString.Value,
            DownstreamUrl = downstreamUrl,
            StatusCode = context.Response.StatusCode,
            ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
            ResponseHeaders = _sanitizer.SanitizeHeaders(context.Response.Headers),
            ResponseBody = responseText,
            ResponseBodyTruncated = responseTruncated
        });
    }

    /// <summary>
    /// Restores the original response stream.
    /// </summary>
    private static async Task RestoreResponseStreamAsync(
        MemoryStream responseBodyStream,
        Stream originalResponseBody,
        IHttpResponseBodyFeature originalBodyFeature,
        HttpContext context)
    {
        responseBodyStream.Seek(0, SeekOrigin.Begin);
        await responseBodyStream.CopyToAsync(originalResponseBody);
        context.Response.Body = originalResponseBody;
        context.Features.Set(originalBodyFeature);
    }

    /// <summary>
    /// Determines if a request should be logged based on sampling and filtering rules.
    /// Uses cached configuration values for optimal performance.
    /// </summary>
    private bool ShouldLog(HttpContext context, string? routeId)
    {
        // Fast path: check minimum log level first (cheapest check)
        if (!_minLogLevelChecked)
        {
            // Cache the min log level - this only runs once
            const int DefaultLevel = 0; // Debug/Verbose
            _minLogLevel = DefaultLevel;
            _minLogLevelChecked = true;
        }

        // Current log level based on status code
        var currentLevel = context.Response.StatusCode switch
        {
            >= 500 => 3,  // Error
            >= 400 => 2,  // Warning
            _ => 1        // Information
        };
        if (currentLevel < _minLogLevel)
        {
            return false;
        }

        // Check errors only mode (cached value)
        if (_logErrorsOnly && context.Response.StatusCode < 400)
        {
            return false;
        }

        // Check route whitelist (cached HashSet)
        if (_routeWhitelist != null)
        {
            if (string.IsNullOrEmpty(routeId) || !_routeWhitelist.Contains(routeId))
            {
                return false;
            }
        }

        // Check route blacklist (cached HashSet)
        if (_routeBlacklist != null && !string.IsNullOrEmpty(routeId) && _routeBlacklist.Contains(routeId))
        {
            return false;
        }

        // Check sampling (cached values, thread-local Random)
        if (_enableSampling && _samplingRate < 1.0)
        {
            if (_threadRandom.Value!.NextDouble() > _samplingRate)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets log level based on HTTP status code.
    /// </summary>
    private static string GetLogLevel(int statusCode)
    {
        return statusCode switch
        {
            >= 500 => "Error",
            >= 400 => "Warning",
            _ => "Information"
        };
    }

    private static string? GetDownstreamBody(HttpContext context)
    {
        if (context.Items.TryGetValue("DownstreamBody", out var obj) && obj is byte[] bodyBytes && bodyBytes.Length > 0)
        {
            return Encoding.UTF8.GetString(bodyBytes);
        }
        return null;
    }

    private static string? GetDownstreamMethod(HttpContext context)
    {
        if (context.Items.TryGetValue("DownstreamMethod", out var obj) && obj is string method)
        {
            return method;
        }
        return null;
    }

    private static string? GetDownstreamUrl(HttpContext context)
    {
        if (context.Items.TryGetValue("DownstreamUrl", out var obj) && obj is string url)
        {
            return url;
        }
        return null;
    }

    private static string? BuildDownstreamUrl(IReverseProxyFeature? proxy, HttpRequest request)
    {
        if (proxy?.ProxiedDestination?.Model?.Config?.Address is not { } baseUrl)
            return null;

        baseUrl = baseUrl.TrimEnd('/');
        var originalPath = request.Path.Value ?? "/";
        var downstreamPath = originalPath;
        var matchPath = proxy.Route?.Config?.Match?.Path;

        // Extract catch-all value from the match pattern
        var catchAllValue = ExtractCatchAllValue(originalPath, matchPath);

        // Apply transforms in order
        var transforms = proxy.Route?.Config?.Transforms;
        if (transforms != null)
        {
            foreach (var tf in transforms)
            {
                if (tf.TryGetValue("PathRemovePrefix", out var prefix) && !string.IsNullOrEmpty(prefix))
                {
                    if (downstreamPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        downstreamPath = downstreamPath[prefix.Length..];
                        if (downstreamPath.Length == 0) downstreamPath = "/";
                    }
                }
                else if (tf.TryGetValue("PathPrefix", out var pathPrefix) && !string.IsNullOrEmpty(pathPrefix))
                {
                    downstreamPath = pathPrefix + downstreamPath;
                }

                string? pathPattern;
                if (tf.TryGetValue("PathSet", out var set) && !string.IsNullOrEmpty(set))
                    pathPattern = set;
                else if (tf.TryGetValue("PathPattern", out var pp) && !string.IsNullOrEmpty(pp))
                    pathPattern = pp;
                else
                    pathPattern = null;

                if (pathPattern != null)
                {
                    if (catchAllValue != null && pathPattern.Contains("{**catch-all}"))
                        downstreamPath = pathPattern.Replace("{**catch-all}", catchAllValue);
                    else
                        downstreamPath = pathPattern;
                }
            }
        }

        return baseUrl + downstreamPath + request.QueryString;
    }

    private static string? ExtractCatchAllValue(string requestPath, string? matchPath)
    {
        if (string.IsNullOrEmpty(matchPath))
            return null;

        // Find the catch-all pattern: {**...}
        var idx = matchPath.IndexOf("{**", StringComparison.Ordinal);
        if (idx < 0)
            return null;

        var prefix = matchPath[..idx];
        if (requestPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var value = requestPath[prefix.Length..];
            return value.TrimStart('/');
        }

        return null;
    }

    // Max body size to read (64KB) - prevents memory issues with large uploads/downloads
    private const int MaxBodySizeBytes = 64 * 1024;

    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength == null || request.ContentLength == 0)
            return string.Empty;

        // Skip large request bodies to avoid memory pressure
        if (request.ContentLength > MaxBodySizeBytes)
            return $"[{request.ContentType}] ({request.ContentLength} bytes) - too large to log";

        if (!request.HasJsonContentType())
            return $"[{request.ContentType}] ({request.ContentLength} bytes)";

        request.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Seek(0, SeekOrigin.Begin);
        return body;
    }

    private static async Task<string> ReadStreamAsync(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);

        // Limit response body reading to prevent memory issues
        if (stream.Length > MaxBodySizeBytes)
        {
            // Use ArrayPool to reduce char[] allocations
            var buffer = ArrayPool<char>.Shared.Rent(MaxBodySizeBytes);
            try
            {
                using var reader = new StreamReader(stream, leaveOpen: true);
                var read = await reader.ReadAsync(buffer, 0, MaxBodySizeBytes);
                return new string(buffer, 0, read) + "\n... [TRUNCATED - response too large]";
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        using var readerFull = new StreamReader(stream, leaveOpen: true);
        return await readerFull.ReadToEndAsync();
    }
}
