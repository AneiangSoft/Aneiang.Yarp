using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Infrastructure;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _extraScriptSources;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<WafOptions> wafOptions)
    {
        _next = next;
        _extraScriptSources = wafOptions.Value.ExtraScriptSources;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Register OnStarting callback so headers are set just before the response is sent.
        context.Response.OnStarting(state =>
        {
            var self = (SecurityHeadersMiddleware)state!;
            self.ApplyHeaders(context.Response);
            return Task.CompletedTask;
        }, this);

        await _next(context);
    }

    private void ApplyHeaders(HttpResponse resp)
    {
        if (!resp.Headers.ContainsKey("X-Content-Type-Options"))
            resp.Headers["X-Content-Type-Options"] = "nosniff";
        if (!resp.Headers.ContainsKey("X-Frame-Options"))
            resp.Headers["X-Frame-Options"] = "DENY";
        if (!resp.Headers.ContainsKey("X-XSS-Protection"))
            resp.Headers["X-XSS-Protection"] = "1; mode=block";
        if (!resp.Headers.ContainsKey("Referrer-Policy"))
            resp.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // CSP for proxied responses — dashboard pages set their own CSP upstream.
        if (!resp.Headers.ContainsKey("Content-Security-Policy"))
        {
            var csp = "default-src 'self'";
            if (!string.IsNullOrWhiteSpace(_extraScriptSources))
                csp += "; script-src 'self' " + _extraScriptSources;
            resp.Headers["Content-Security-Policy"] = csp;
        }
    }
}
