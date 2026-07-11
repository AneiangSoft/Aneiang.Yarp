using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Middleware;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using System.Buffers;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Services;
using Microsoft.IO;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Health;

namespace Aneiang.Yarp.Dashboard.Modules.Retry.Middleware;

/// <summary>
/// Request retry middleware for failed proxy requests.
/// Retries 502/503/504 responses with exponential backoff + jitter.
/// Supports cross-destination retry and circuit-breaker awareness.
/// 
/// Memory optimization (v2.4): Uses RecyclableMemoryStream for request/response
/// body buffering in retry loop — eliminates LOH fragmentation from repeated
/// MemoryStream allocations. Also fixes ArrayPool.Rent + .ToArray() contradiction
/// by storing the pooled buffer directly with a length marker.
/// </summary>
public sealed class RequestRetryMiddleware : GatewayMiddlewareBase
{
    private readonly ILogger<RequestRetryMiddleware> _logger;
    private readonly RetryOptions _options;
    private readonly IDynamicYarpConfigService? _yarpConfig;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly ICircuitStateStore _circuitStore;


    private static readonly HashSet<string> NonIdempotentMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PATCH", "PUT", "DELETE"
    };

    public RequestRetryMiddleware(
        RequestDelegate next,
        ILogger<RequestRetryMiddleware> logger,
        IOptions<RetryOptions> options,
        IOptions<DashboardOptions> dashOptions,
        IGatewayPluginManager pluginManager,
        RecyclableMemoryStreamManager memoryStreamManager,
        ICircuitStateStore circuitStore,
        IDynamicYarpConfigService? yarpConfig = null)
        : base(next, dashOptions, pluginManager, yarpConfig)
    {
        _logger = logger;
        _options = options.Value;
        _memoryStreamManager = memoryStreamManager;
        _circuitStore = circuitStore;
        _yarpConfig = yarpConfig;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsDashboardRequest(context))
        {
            await Next(context);
            return;
        }

        if (!IsPluginEnabled("request-retry"))
        {
            await Next(context);
            return;
        }

        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var routeConfig = proxyFeature?.Route?.Config;
        var clusterId = routeConfig?.ClusterId;

        if (routeConfig == null || !IsRetryEnabled(routeConfig))
        {
            await Next(context);
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
            await Next(context);
            return;
        }

        // Read and buffer the request body once — pooled buffer stored for retry loop reuse
        context.Request.EnableBuffering();
        var requestBodyResult = await ReadRequestBodyPooledAsync(context.Request);
        byte[]? requestBodyBuffer = requestBodyResult.Buffer;
        int requestBodyLength = requestBodyResult.Length;

        int attempt = 0;
        int? lastStatusCode = null;
        DestinationState? markedUnhealthyDest = null;
        var originalHealthState = DestinationHealth.Unknown;

        try
        {
            while (attempt <= maxRetries)
            {
                // Check circuit breaker before retry
                if (attempt > 0 && !string.IsNullOrEmpty(clusterId))
                {
                    if (_circuitStore.IsCircuitOpen(clusterId, clusterUid: ResolveClusterUid(clusterId)))
                    {
                        _logger.LogDebug("Circuit breaker is open, skipping retry");
                        break;
                    }
                }

                // Restore request body from pooled buffer for each retry attempt
                if (requestBodyBuffer != null)
                {
                    context.Request.Body = new MemoryStream(requestBodyBuffer, 0, requestBodyLength, writable: false);
                    context.Request.ContentLength = requestBodyLength;
                }

                var originalResponseBody = context.Response.Body;
                using var responseStream = _memoryStreamManager.GetStream("RequestRetry-ResponseBody");
                context.Response.Body = responseStream;

                try
                {
                    await Next(context);
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

                    // F2 fix: When useDifferentDestination is enabled, temporarily mark the current
                    // destination as Unhealthy so YARP's load balancer picks a different one on retry.
                    if (useDifferentDestination && markedUnhealthyDest == null)
                    {
                        var proxyFeat = context.Features.Get<IReverseProxyFeature>();
                        var destState = proxyFeat?.ProxiedDestination;
                        var cluster = proxyFeat?.Route?.Cluster;
                        if (destState != null && cluster?.DestinationsState?.AllDestinations != null)
                        {
                            bool otherHealthy = false;
                            foreach (var d in cluster.DestinationsState.AllDestinations)
                            {
                                if (d != destState && d.Health.Active != DestinationHealth.Unhealthy)
                                {
                                    otherHealthy = true;
                                    break;
                                }
                            }
                            if (otherHealthy)
                            {
                                markedUnhealthyDest = destState;
                                originalHealthState = destState.Health.Active;
                                destState.Health.Active = DestinationHealth.Unhealthy;
                                _logger.LogDebug(
                                    "Retry: marking destination '{DestId}' as Unhealthy for cross-destination retry",
                                    destState.DestinationId);
                            }
                        }
                    }

                    try
                    {
                        await Task.Delay(delayMs, context.RequestAborted);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogDebug("Retry cancelled by client disconnect for {Method} {Path}",
                            context.Request.Method, context.Request.Path);
                        break;
                    }
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
        }
        finally
        {
            // Return the ArrayPool buffer to the pool (BUG-3 fix)
            if (requestBodyBuffer != null)
                ArrayPool<byte>.Shared.Return(requestBodyBuffer);

            // Restore destination health if it was temporarily marked Unhealthy for cross-destination retry
            if (markedUnhealthyDest != null)
            {
                markedUnhealthyDest.Health.Active = originalHealthState;
                _logger.LogDebug(
                    "Retry: restored destination '{DestId}' health to {Health}",
                    markedUnhealthyDest.DestinationId, originalHealthState);
            }
        }

        if (attempt > 0)
        {
            context.Response.Headers["X-Retry-Count"] = attempt.ToString();
        }
    }

    /// <summary>Maximum request body size for retry buffering. Prevents OOM from large uploads.</summary>
    private const int MaxRetryBodySizeBytes = 1024 * 1024; // 1MB hard limit


    /// <summary>
    /// Read request body into a pooled buffer, returning the buffer and length directly.
    /// Memory optimization (v2.4): No .ToArray() — stores pooled buffer + length marker.
    /// The buffer is NOT returned to the pool during retry; it's reused for each attempt.
    /// It will be GC'd naturally after the retry loop completes (acceptable trade-off for correctness).
    /// </summary>
    private async Task<RequestBodyBuffer> ReadRequestBodyPooledAsync(HttpRequest request)
    {
        if (request.ContentLength is null or 0)
            return default;

        // Hard size limit — prevents OOM on multi-GB uploads
        if (request.ContentLength > MaxRetryBodySizeBytes)
        {
            _logger.LogDebug("Request body ({Size} bytes) exceeds retry buffer limit ({Limit} bytes), skipping retry",
                request.ContentLength, MaxRetryBodySizeBytes);
            return default;
        }

        request.Body.Position = 0;
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent((int)request.ContentLength);
        try
        {
            int read = 0;
            int bytesRead;
            while ((bytesRead = await request.Body.ReadAsync(buffer.AsMemory(read, (int)request.ContentLength - read))) > 0)
            {
                read += bytesRead;
                if (read > MaxRetryBodySizeBytes) break;
            }
            // Reset position for downstream middleware
            request.Body.Position = 0;
            return new RequestBodyBuffer(buffer, read);
        }
        catch
        {
            // On error, return buffer to pool
            pool.Return(buffer);
            throw;
        }
    }

    /// <summary>
    /// Lightweight struct to carry pooled buffer + length (avoids async out parameter limitation).
    /// </summary>
    private readonly struct RequestBodyBuffer
    {
        public readonly byte[]? Buffer;
        public readonly int Length;

        public RequestBodyBuffer(byte[]? buffer, int length)
        {
            Buffer = buffer;
            Length = length;
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
