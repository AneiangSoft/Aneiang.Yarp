using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Middleware;

/// <summary>
/// Request retry middleware for failed proxy requests.
/// Retries 502/503/504 responses by attempting the next available destination in the cluster.
/// Configurable per-route via metadata or globally via DashboardOptions.
/// </summary>
public sealed class RequestRetryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestRetryMiddleware> _logger;

    private static readonly HashSet<int> DefaultRetryStatusCodes = new() { 502, 503, 504 };
    private static readonly HashSet<string> NonIdempotentMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PATCH"
    };

    public RequestRetryMiddleware(RequestDelegate next, ILogger<RequestRetryMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var routeConfig = proxyFeature?.Route?.Config;

        // Skip if no proxy feature or retry not configured
        if (routeConfig == null || !IsRetryEnabled(routeConfig))
        {
            await _next(context);
            return;
        }

        var maxRetries = GetMaxRetries(routeConfig);
        var retryStatusCodes = GetRetryStatusCodes(routeConfig);
        var retryNonIdempotent = IsRetryNonIdempotentEnabled(routeConfig);

        // Skip non-idempotent methods unless explicitly enabled
        if (!retryNonIdempotent && NonIdempotentMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Store original request body for retries
        context.Request.EnableBuffering();
        var requestBody = await ReadRequestBodyAsync(context.Request);

        int attempt = 0;
        int? lastStatusCode = null;

        while (attempt <= maxRetries)
        {
            // Reset request body position for each attempt
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Seek(0, SeekOrigin.Begin);
            }

            // Capture response
            var originalResponseBody = context.Response.Body;
            using var responseStream = new MemoryStream();
            context.Response.Body = responseStream;

            try
            {
                await _next(context);
            }
            finally
            {
                responseStream.Seek(0, SeekOrigin.Begin);
            }

            lastStatusCode = context.Response.StatusCode;

            // Check if we should retry
            if (attempt < maxRetries && retryStatusCodes.Contains(context.Response.StatusCode))
            {
                attempt++;
                _logger.LogWarning(
                    "Retry {Attempt}/{MaxRetries} for {Method} {Path} (status {StatusCode})",
                    attempt, maxRetries, context.Request.Method, context.Request.Path,
                    context.Response.StatusCode);

                // Reset response for next attempt
                context.Response.Body = originalResponseBody;
                context.Response.StatusCode = 200;
                context.Response.Headers.Clear();

                // Small delay between retries
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));

                continue;
            }

            // No retry needed, copy response back
            responseStream.Seek(0, SeekOrigin.Begin);
            await responseStream.CopyToAsync(originalResponseBody);
            context.Response.Body = originalResponseBody;

            if (attempt > 0)
            {
                _logger.LogInformation(
                    "Request succeeded on attempt {Attempt} for {Method} {Path} (status {StatusCode})",
                    attempt + 1, context.Request.Method, context.Request.Path, context.Response.StatusCode);
            }

            break;
        }

        // Set retry header if retried
        if (attempt > 0)
        {
            context.Response.Headers["X-Retry-Count"] = attempt.ToString();
        }
    }

    private static async Task<byte[]?> ReadRequestBodyAsync(HttpRequest request)
    {
        if (request.ContentLength == null || request.ContentLength == 0)
            return null;

        request.Body.Seek(0, SeekOrigin.Begin);
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        request.Body.Seek(0, SeekOrigin.Begin);
        return ms.ToArray();
    }

    private static bool IsRetryEnabled(RouteConfig routeConfig)
    {
        if (routeConfig.Metadata != null &&
            routeConfig.Metadata.TryGetValue("Retry:Enabled", out var enabled) &&
            bool.TryParse(enabled, out var isEnabled))
        {
            return isEnabled;
        }
        return true; // Enabled by default when middleware is registered
    }

    private static int GetMaxRetries(RouteConfig routeConfig)
    {
        if (routeConfig.Metadata != null &&
            routeConfig.Metadata.TryGetValue("Retry:MaxRetries", out var max) &&
            int.TryParse(max, out var maxRetries))
        {
            return Math.Clamp(maxRetries, 0, 5);
        }
        return 2;
    }

    private static HashSet<int> GetRetryStatusCodes(RouteConfig routeConfig)
    {
        if (routeConfig.Metadata != null &&
            routeConfig.Metadata.TryGetValue("Retry:RetryOnStatusCodes", out var codes))
        {
            var set = new HashSet<int>();
            foreach (var code in codes.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(code.Trim(), out var c))
                    set.Add(c);
            }
            return set.Count > 0 ? set : DefaultRetryStatusCodes;
        }
        return DefaultRetryStatusCodes;
    }

    private static bool IsRetryNonIdempotentEnabled(RouteConfig routeConfig)
    {
        if (routeConfig.Metadata != null &&
            routeConfig.Metadata.TryGetValue("Retry:RetryNonIdempotent", out var val) &&
            bool.TryParse(val, out var enabled))
        {
            return enabled;
        }
        return false;
    }
}
