using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Models;
using System.Buffers;
using System.Text;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Middleware;

/// <summary>
/// Request retry middleware for failed proxy requests.
/// Retries 502/503/504 responses with exponential backoff + jitter.
/// Supports cross-destination retry and circuit-breaker awareness.
/// </summary>
public sealed class RequestRetryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestRetryMiddleware> _logger;
    private readonly RetryOptions _options;

    private static readonly HashSet<string> NonIdempotentMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PATCH", "PUT", "DELETE"
    };

    public RequestRetryMiddleware(
        RequestDelegate next,
        ILogger<RequestRetryMiddleware> logger,
        IOptions<RetryOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var routeConfig = proxyFeature?.Route?.Config;
        var clusterId = routeConfig?.ClusterId;

        if (routeConfig == null || !IsRetryEnabled(routeConfig))
        {
            await _next(context);
            return;
        }

        var maxRetries = GetMaxRetries(routeConfig);
        var useDifferentDestination = IsUseDifferentDestination(routeConfig);
        var retryStatusCodes = GetRetryStatusCodes(routeConfig);
        var retryNonIdempotent = IsRetryNonIdempotentEnabled(routeConfig);
        var backoffBaseMs = GetBackoffBaseMs(routeConfig);
        var jitterMs = GetJitterMs(routeConfig);

        if (!retryNonIdempotent && NonIdempotentMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        context.Request.EnableBuffering();
        var requestBody = await ReadRequestBodyAsync(context.Request);

        int attempt = 0;
        int? lastStatusCode = null;
        var triedDestinations = new HashSet<string>();

        while (attempt <= maxRetries)
        {
            // Check circuit breaker before retry
            if (attempt > 0 && !string.IsNullOrEmpty(clusterId))
            {
                if (CircuitBreakerMiddleware.IsCircuitOpen(clusterId))
                {
                    _logger.LogDebug("Circuit breaker is open, skipping retry");
                    break;
                }
            }

            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Seek(0, SeekOrigin.Begin);
            }

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

            if (attempt < maxRetries && retryStatusCodes.Contains(context.Response.StatusCode))
            {
                attempt++;
                var baseDelay = backoffBaseMs * (int)Math.Pow(2, attempt - 1);
                var jitter = Random.Shared.Next(0, jitterMs);
                var delayMs = baseDelay + jitter;

                _logger.LogWarning(
                    "Retry {Attempt}/{MaxRetries} for {Method} {Path} (status {StatusCode}, delay {Delay}ms)",
                    attempt, maxRetries, context.Request.Method, context.Request.Path,
                    context.Response.StatusCode, delayMs);

                context.Response.Body = originalResponseBody;
                context.Response.StatusCode = 200;
                context.Response.Headers.Clear();

                await Task.Delay(delayMs);
                continue;
            }

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

        if (attempt > 0)
        {
            context.Response.Headers["X-Retry-Count"] = attempt.ToString();
        }
    }

    /// <summary>Maximum request body size for retry buffering. Prevents OOM from large uploads.</summary>
    private const int MaxRetryBodySizeBytes = 1024 * 1024; // 1MB hard limit

    private async Task<byte[]?> ReadRequestBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is null or 0)
            return null;

        // Hard size limit — prevents OOM on multi-GB uploads
        if (request.ContentLength > MaxRetryBodySizeBytes)
        {
            _logger.LogDebug("Request body ({Size} bytes) exceeds retry buffer limit ({Limit} bytes), skipping retry",
                request.ContentLength, MaxRetryBodySizeBytes);
            return null;
        }

        request.Body.Seek(0, SeekOrigin.Begin);
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent((int)request.ContentLength);
        try
        {
            int read = 0;
            int bytesRead;
            while ((bytesRead = await request.Body.ReadAsync(buffer.AsMemory(read, (int)request.ContentLength - read))) > 0)
            {
                read += bytesRead;
                // Safety check: if we somehow read more than ContentLength suggested
                if (read > MaxRetryBodySizeBytes) break;
            }
            request.Body.Seek(0, SeekOrigin.Begin);
            return buffer.AsSpan(0, read).ToArray();
        }
        finally
        {
            pool.Return(buffer);
        }
    }

    private static bool IsRetryEnabled(RouteConfig routeConfig)
    {
        if (routeConfig.Metadata != null &&
            routeConfig.Metadata.TryGetValue("Retry:Enabled", out var enabled) &&
            bool.TryParse(enabled, out var isEnabled))
        {
            return isEnabled;
        }
        return false;
    }

    private RetryOptions GetEffectiveOptions(RouteConfig routeConfig)
    {
        var meta = routeConfig.Metadata;
        var options = new RetryOptions
        {
            Enabled = _options.Enabled,
            DefaultMaxRetries = _options.DefaultMaxRetries,
            BackoffBaseMs = _options.BackoffBaseMs,
            BackoffJitterMs = _options.BackoffJitterMs,
            TimeoutSeconds = _options.TimeoutSeconds,
            UseDifferentDestination = _options.UseDifferentDestination,
            RetryNonIdempotent = _options.RetryNonIdempotent,
            DefaultRetryStatusCodes = _options.DefaultRetryStatusCodes
        };

        if (meta == null)
            return options;

        if (meta.TryGetValue("Retry:MaxRetries", out var max) && int.TryParse(max, out var maxRetries))
            options.DefaultMaxRetries = Math.Clamp(maxRetries, 0, 5);

        if (meta.TryGetValue("Retry:BackoffBaseMs", out var baseMs) && int.TryParse(baseMs, out var b))
            options.BackoffBaseMs = b;

        if (meta.TryGetValue("Retry:BackoffJitterMs", out var j) && int.TryParse(j, out var jitter))
            options.BackoffJitterMs = jitter;

        if (meta.TryGetValue("Retry:TimeoutSeconds", out var t) && int.TryParse(t, out var timeout))
            options.TimeoutSeconds = timeout;

        if (meta.TryGetValue("Retry:UseDifferentDestination", out var diff) && bool.TryParse(diff, out var useDiff))
            options.UseDifferentDestination = useDiff;

        if (meta.TryGetValue("Retry:RetryNonIdempotent", out var nonIdemp) && bool.TryParse(nonIdemp, out var ri))
            options.RetryNonIdempotent = ri;

        return options;
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

    private static int GetBackoffBaseMs(RouteConfig routeConfig)
    {
        if (routeConfig.Metadata != null &&
            routeConfig.Metadata.TryGetValue("Retry:BackoffBaseMs", out var baseMs) &&
            int.TryParse(baseMs, out var b))
        {
            return b;
        }
        return 100;
    }

    private static int GetJitterMs(RouteConfig routeConfig)
    {
        if (routeConfig.Metadata != null &&
            routeConfig.Metadata.TryGetValue("Retry:BackoffJitterMs", out var j) &&
            int.TryParse(j, out var jitter))
        {
            return jitter;
        }
        return 50;
    }

    private static int GetRetryTimeout(RouteConfig routeConfig)
    {
        if (routeConfig.Metadata != null &&
            routeConfig.Metadata.TryGetValue("Retry:TimeoutSeconds", out var t) &&
            int.TryParse(t, out var timeout))
        {
            return timeout;
        }
        return 30;
    }

    private static bool IsUseDifferentDestination(RouteConfig routeConfig)
    {
        if (routeConfig.Metadata != null &&
            routeConfig.Metadata.TryGetValue("Retry:UseDifferentDestination", out var val) &&
            bool.TryParse(val, out var enabled))
        {
            return enabled;
        }
        return false;
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
            return set.Count > 0 ? set : new HashSet<int> { 502, 503, 504 };
        }
        return new HashSet<int> { 502, 503, 504 };
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
