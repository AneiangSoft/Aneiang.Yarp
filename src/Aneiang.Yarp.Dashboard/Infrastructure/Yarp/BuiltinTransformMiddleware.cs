using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Alert.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Models;
using Aneiang.Yarp.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Yarp;

/// <summary>
/// Built-in request/response transform middleware.
/// Adds common headers, removes security-sensitive headers, and applies global transforms.
/// Runs before YARP proxy pipeline.
/// </summary>
public sealed class BuiltinTransformMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BuiltinTransformOptions _options;
    private readonly string _dashPrefix;
    /// <summary>
    /// Content root path for the Dashboard static files. Used to skip logging for frontend resources.
    /// </summary>
    private const string ContentRoot = "/_content/Aneiang.Yarp.Dashboard";

    public BuiltinTransformMiddleware(RequestDelegate next, IOptions<BuiltinTransformOptions> options, IOptions<DashboardOptions> dashboardOptions)
    {
        _next = next;
        _options = options.Value;
        _dashPrefix = "/" + dashboardOptions.Value.RoutePrefix.Trim('/');
    }

    public async Task InvokeAsync(HttpContext context)
    {
        //Skip Dashboard requests
        if (context.Request.Path.StartsWithSegments(_dashPrefix, StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments(ContentRoot, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

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
