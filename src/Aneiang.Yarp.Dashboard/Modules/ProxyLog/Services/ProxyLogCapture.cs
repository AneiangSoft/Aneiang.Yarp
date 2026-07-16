using System.Diagnostics;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Default implementation that creates <see cref="LogEntry"/> records and adds them to <see cref="IProxyLogStore"/>.
/// </summary>
public sealed class ProxyLogCapture : IProxyLogCapture
{
    private readonly IProxyLogStore _store;
    private readonly LogSanitizer _sanitizer;

    public ProxyLogCapture(IProxyLogStore store, LogSanitizer sanitizer)
    {
        _store = store;
        _sanitizer = sanitizer;
    }

    /// <summary>
    /// Builds and stores both ProxyRequest and ProxyResponse log entries.
    /// </summary>
    public void CaptureLogEntry(
        HttpContext context,
        IReverseProxyFeature? proxyFeature,
        string upstreamPath,
        string? routeId,
        string? clusterId,
        DateTime timestamp,
        TimeSpan elapsed,
        HeaderList? requestHeaders,
        string requestBody,
        bool requestBodyTruncated,
        string? responseBodyText,
        bool responseTruncated,
        HeaderList? responseHeaders,
        string? downstreamText,
        bool downstreamTruncated)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        var sanitizedRequestBody = _sanitizer.SanitizeJsonBody(requestBody);
        var requestText = _sanitizer.TruncateText(sanitizedRequestBody, out _);

        var sanitizedResponseBody = _sanitizer.SanitizeJsonBody(responseBodyText ?? string.Empty);
        var responseText = _sanitizer.TruncateText(sanitizedResponseBody, out _);

        _store.Add(new LogEntry
        {
            Timestamp = timestamp,
            EventType = LogEventType.ProxyRequest,
            Level = "Information",
            Message = null,
            TraceId = traceId,
            Method = context.Request.Method,
            UpstreamPath = upstreamPath,
            RequestHeaders = requestHeaders,
            RequestBody = requestText,
            RequestBodyTruncated = requestBodyTruncated
        });

        var downstreamUrl = ProxyLogBodyReader.GetDownstreamUrl(context)
            ?? BuildDownstreamUrl(proxyFeature, context.Request);
        var downstreamMethod = ProxyLogBodyReader.GetDownstreamMethod(context);

        _store.Add(new LogEntry
        {
            Timestamp = DateTime.Now,
            EventType = LogEventType.ProxyResponse,
            Level = ProxyLogBodyReader.GetLogLevel(context.Response.StatusCode),
            Message = null,
            TraceId = traceId,
            RouteId = routeId,
            ClusterId = clusterId,
            Method = context.Request.Method,
            UpstreamPath = upstreamPath,
            DownstreamUrl = downstreamUrl,
            DownstreamMethod = downstreamMethod,
            DownstreamBody = downstreamText,
            DownstreamBodyTruncated = downstreamTruncated,
            StatusCode = context.Response.StatusCode,
            ElapsedMs = elapsed.TotalMilliseconds,
            ResponseHeaders = responseHeaders,
            ResponseBody = responseText,
            ResponseBodyTruncated = responseTruncated
        });
    }

    private static string? BuildDownstreamUrl(IReverseProxyFeature? proxy, HttpRequest request)
    {
        if (proxy?.ProxiedDestination?.Model?.Config?.Address is not { } baseUrl)
            return null;

        baseUrl = baseUrl.TrimEnd('/');
        var originalPath = request.Path.Value ?? "/";
        var downstreamPath = originalPath;
        var matchPath = proxy.Route?.Config?.Match?.Path;

        var catchAllValue = ExtractCatchAllValue(originalPath, matchPath);

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

        var idx = matchPath.IndexOf("{**", StringComparison.Ordinal);
        if (idx < 0)
            return null;

        var prefix = matchPath[..idx];
        if (requestPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return requestPath[prefix.Length..].TrimStart('/');

        return null;
    }
}
