using Aneiang.Yarp.Dashboard.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Captures incoming proxy request/response details before YARP processes the request.
/// Skips Dashboard requests. Buffers body for parameter capture.
/// Supports structured logging, sampling, filtering, and sanitization.
/// Zero dependency on logging frameworks.
/// </summary>
public sealed class YarpRequestCaptureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IProxyLogStore _store;
    private readonly LogSanitizer _sanitizer;
    private readonly string _dashPrefix;
    private readonly bool _loggingEnabled;
    private readonly DashboardOptions _options;
    private readonly Random _random = new();

    /// <summary>
    /// Creates the middleware.
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
        _dashPrefix = "/" + options.Value.RoutePrefix.Trim('/');
        _loggingEnabled = options.Value.EnableProxyLogging;
        _options = options.Value;
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
        if (context.Request.Path.StartsWithSegments(_dashPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var timestamp = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        // Enable request body buffering for capture
        context.Request.EnableBuffering();

        // Read request body (non-consuming)
        string requestBody = await ReadBodyAsync(context.Request);

        // Capture original response body stream
        var originalResponseBody = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context);
        stopwatch.Stop();

        // Read response body
        string responseBodyText = await ReadStreamAsync(responseBody);

        // Get downstream destination address from YARP proxy feature
        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var downstreamUrl = BuildDownstreamUrl(proxyFeature, context.Request);
        var routeId = proxyFeature?.Route?.Config?.RouteId;
        var clusterId = proxyFeature?.Route?.Config?.ClusterId;

        // Get captured downstream request body (after transforms like encryption)
        var downstreamBody = GetDownstreamBody(context);

        // Restore original response stream and copy back
        responseBody.Seek(0, SeekOrigin.Begin);
        await responseBody.CopyToAsync(originalResponseBody);
        context.Response.Body = originalResponseBody;

        // Check if should log based on sampling and filtering rules
        if (!ShouldLog(context, routeId))
        {
            return;
        }

        // Sanitize and truncate request body
        var sanitizedRequestBody = _sanitizer.SanitizeJsonBody(requestBody);
        var requestText = _sanitizer.TruncateText(sanitizedRequestBody, out var requestTruncated);

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
            UpstreamPath = context.Request.Path + context.Request.QueryString.Value,
            DownstreamUrl = downstreamUrl,
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
    /// Determines if a request should be logged based on sampling and filtering rules.
    /// </summary>
    private bool ShouldLog(HttpContext context, string? routeId)
    {
        // Check route whitelist
        if (_options.LogRouteWhitelist?.Count > 0)
        {
            if (string.IsNullOrEmpty(routeId) || 
                !_options.LogRouteWhitelist.Contains(routeId, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Check route blacklist
        if (_options.LogRouteBlacklist?.Count > 0 && 
            !string.IsNullOrEmpty(routeId) &&
            _options.LogRouteBlacklist.Contains(routeId, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check errors only mode
        if (_options.LogErrorsOnly && context.Response.StatusCode < 400)
        {
            return false;
        }

        // Check sampling
        if (_options.EnableLogSampling && _options.LogSamplingRate < 1.0)
        {
            if (_random.NextDouble() > _options.LogSamplingRate)
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

    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength == null || request.ContentLength == 0)
            return string.Empty;

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
        using var reader = new StreamReader(stream, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        return text;
    }
}
