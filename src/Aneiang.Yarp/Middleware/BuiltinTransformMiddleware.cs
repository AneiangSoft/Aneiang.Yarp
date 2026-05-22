using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Middleware;

/// <summary>
/// Built-in request/response transform middleware.
/// Adds common headers, removes security-sensitive headers, and applies global transforms.
/// Runs before YARP proxy pipeline.
/// </summary>
public sealed class BuiltinTransformMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BuiltinTransformOptions _options;

    public BuiltinTransformMiddleware(RequestDelegate next, IOptions<BuiltinTransformOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // ─── Request transforms ───

        // Add X-Request-Id if not present
        if (_options.EnableRequestIdHeader &&
            !context.Request.Headers.ContainsKey("X-Request-Id"))
        {
            var requestId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
            context.Request.Headers["X-Request-Id"] = requestId;
        }

        // Add X-Forwarded-For
        if (_options.EnableForwardedForHeader)
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(remoteIp))
            {
                var existing = context.Request.Headers["X-Forwarded-For"].ToString();
                context.Request.Headers["X-Forwarded-For"] =
                    string.IsNullOrEmpty(existing) ? remoteIp : $"{existing}, {remoteIp}";
            }
        }

        await _next(context);

        // ─── Response transforms (after proxy) ───

        // Remove Server header
        if (_options.RemoveServerHeader)
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.Remove("Server");
                return Task.CompletedTask;
            });
        }

        // Remove X-Powered-By header
        if (_options.RemovePoweredByHeader)
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.Remove("X-Powered-By");
                return Task.CompletedTask;
            });
        }

        // Add custom response headers
        if (_options.AddResponseHeaders != null)
        {
            context.Response.OnStarting(() =>
            {
                foreach (var (key, value) in _options.AddResponseHeaders)
                {
                    if (!context.Response.Headers.ContainsKey(key))
                    {
                        context.Response.Headers[key] = value;
                    }
                }
                return Task.CompletedTask;
            });
        }
    }
}
