using Aneiang.Yarp.Dashboard.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Text;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Captures incoming proxy request/response details before YARP processes the request.
/// Skips Dashboard requests. Buffers body for parameter capture.
/// Zero dependency on logging frameworks.
/// 捕获代理请求/响应详情，跳过 Dashboard 请求，缓冲正文以捕获参数。零日志框架依赖.
/// </summary>
public sealed class YarpRequestCaptureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ProxyLogStore _store;
    private readonly string _dashPrefix;
    private readonly bool _loggingEnabled;

    /// <summary>
    /// Creates the middleware.
    /// </summary>
    public YarpRequestCaptureMiddleware(RequestDelegate next, ProxyLogStore store, IOptions<DashboardOptions> options)
    {
        _next = next;
        _store = store;
        _dashPrefix = "/" + options.Value.RoutePrefix.Trim('/');
        _loggingEnabled = options.Value.EnableProxyLogging;
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

        // Skip Dashboard requests / 跳过 Dashboard 请求
        if (context.Request.Path.StartsWithSegments(_dashPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var timestamp = DateTime.UtcNow;

        // Enable request body buffering for capture / 启用请求体缓冲
        context.Request.EnableBuffering();

        // Read request body (non-consuming) / 读取请求体（不消耗流）
        string requestBody = await ReadBodyAsync(context.Request);

        // Capture original response body stream / 捕获原始响应流
        var originalResponseBody = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context);

        // Read response body / 读取响应体
        string responseBodyText = await ReadStreamAsync(responseBody);

        // Get downstream destination address from YARP proxy feature / 获取 YARP 下游目标地址
        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var downstreamUrl = BuildDownstreamUrl(proxyFeature, context.Request);
        var dest = downstreamUrl != null ? " \u2192 " + downstreamUrl : null;

        // Get captured downstream request body (after transforms like encryption) / 获取下游实际请求体
        var downstreamBody = GetDownstreamBody(context);

        // Restore original response stream and copy back / 恢复并回写响应流
        responseBody.Seek(0, SeekOrigin.Begin);
        await responseBody.CopyToAsync(originalResponseBody);
        context.Response.Body = originalResponseBody;

       // Build message / 构建日志消息
        var msg = $"[Request] {context.Request.Method} {context.Request.Path}{context.Request.QueryString}{dest}";
        string? requestDetails = null;
        if (!string.IsNullOrEmpty(requestBody))
            requestDetails = $"Request Body:\n{requestBody}";
        if (downstreamUrl != null)
            requestDetails = (requestDetails != null ? requestDetails + "\n" : "") + $"Downstream: {downstreamUrl}";
        if (downstreamBody != null)
            requestDetails = (requestDetails != null ? requestDetails + "\n" : "") + $"Downstream Body:\n{downstreamBody}";

        _store.Add(new LogEntry
        {
            Timestamp = timestamp,
            Level = "Information",
            Category = "Gateway",
            Message = msg,
            Details = requestDetails
        });

        var respMsg = $"[Response] {context.Response.StatusCode} {context.Request.Method} {context.Request.Path}{context.Request.QueryString}{dest}";
        string? responseDetails = null;
        if (!string.IsNullOrEmpty(responseBodyText))
            responseDetails = $"Response Body:\n{responseBodyText}";

        _store.Add(new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = "Information",
            Category = "Gateway",
            Message = respMsg,
            Details = responseDetails
        });
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

        // Extract catch-all value from the match pattern / 从匹配路径中提取 catch-all 值
        var catchAllValue = ExtractCatchAllValue(originalPath, matchPath);

        // Apply transforms in order / 按顺序应用 Transforms
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

/// <summary>
/// Extension methods for checking request content type / 检查请求内容类型的扩展方法.
/// </summary>
internal static class HttpRequestExtensions
{
    public static bool HasJsonContentType(this HttpRequest request)
    {
        var ct = request.ContentType;
        if (string.IsNullOrEmpty(ct)) return false;
        return ct.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
    }
}
