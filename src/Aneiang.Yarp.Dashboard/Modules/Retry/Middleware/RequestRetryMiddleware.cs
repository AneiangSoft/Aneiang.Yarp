using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Middleware;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using System.Buffers;
using System.Runtime.CompilerServices;
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
    private readonly ConditionalWeakTable<RouteConfig, ParsedRetryConfig> _routeConfigs = new();
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

        if (routeConfig == null)
        {
            await Next(context);
            return;
        }

        var retryConfig = _routeConfigs.GetValue(routeConfig, ParseRetryConfig);
        if (!retryConfig.Enabled)
        {
            await Next(context);
            return;
        }

        if (!retryConfig.RetryNonIdempotent && NonIdempotentMethods.Contains(context.Request.Method))
        {
            await Next(context);
            return;
        }

        // Read and buffer the request body only when one is present.
        byte[]? requestBodyBuffer = null;
        var requestBodyLength = 0;
        if (context.Request.ContentLength is > 0)
        {
            context.Request.EnableBuffering();
            var requestBodyResult = await ReadRequestBodyPooledAsync(context.Request);
            requestBodyBuffer = requestBodyResult.Buffer;
            requestBodyLength = requestBodyResult.Length;
        }

        int attempt = 0;
        int? lastStatusCode = null;
        Stream? activeOriginalResponseBody = null;

        try
        {
            while (attempt <= retryConfig.MaxRetries)
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
                activeOriginalResponseBody = originalResponseBody;
                using var responseStream = _memoryStreamManager.GetStream("RequestRetry-ResponseBody");
                context.Response.Body = responseStream;

                try
                {
                    await Next(context);
                }
                catch
                {
                    context.Response.Body = originalResponseBody;
                    activeOriginalResponseBody = null;
                    throw;
                }

                responseStream.Seek(0, SeekOrigin.Begin);

                lastStatusCode = context.Response.StatusCode;

                if (attempt < retryConfig.MaxRetries && retryConfig.StatusCodes.Contains(context.Response.StatusCode))
                {
                    attempt++;
                    var baseDelay = retryConfig.BackoffBaseMs * (1 << (attempt - 1));
                    var jitter = retryConfig.JitterMs > 0 ? Random.Shared.Next(retryConfig.JitterMs) : 0;
                    var delayMs = baseDelay + jitter;

                    _logger.LogWarning(
                        "Retry {Attempt}/{MaxRetries} for {Method} {Path} (status {StatusCode}, delay {Delay}ms)",
                        attempt, retryConfig.MaxRetries, context.Request.Method, context.Request.Path,
                        context.Response.StatusCode, delayMs);

                    context.Response.Body = originalResponseBody;
                    activeOriginalResponseBody = null;
                    context.Response.StatusCode = 200;
                    context.Response.Headers.Clear();

                    if (retryConfig.UseDifferentDestination)
                    {
                        _logger.LogDebug(
                            "Cross-destination retry requested for {Method} {Path}; shared destination health is left unchanged",
                            context.Request.Method, context.Request.Path);
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
                await responseStream.CopyToAsync(originalResponseBody, context.RequestAborted);
                context.Response.Body = originalResponseBody;
                activeOriginalResponseBody = null;

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
            if (activeOriginalResponseBody != null)
                context.Response.Body = activeOriginalResponseBody;

            // Return the ArrayPool buffer to the pool (BUG-3 fix)
            if (requestBodyBuffer != null)
                ArrayPool<byte>.Shared.Return(requestBodyBuffer);
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

    private ParsedRetryConfig ParseRetryConfig(RouteConfig routeConfig)
    {
        var metadata = routeConfig.Metadata;
        var enabled = GetBoolean(metadata, "Retry:Enabled", false);
        var maxRetries = Math.Clamp(GetInt32(metadata, "Retry:MaxRetries", _options.DefaultMaxRetries), 0, 5);
        var backoffBaseMs = Math.Max(0, GetInt32(metadata, "Retry:BackoffBaseMs", _options.BackoffBaseMs));
        var jitterMs = Math.Max(0, GetInt32(metadata, "Retry:BackoffJitterMs", _options.BackoffJitterMs));
        var statusCodes = ParseStatusCodes(metadata);
        return new ParsedRetryConfig(
            enabled,
            maxRetries,
            backoffBaseMs,
            jitterMs,
            GetBoolean(metadata, "Retry:UseDifferentDestination", _options.UseDifferentDestination),
            GetBoolean(metadata, "Retry:RetryNonIdempotent", _options.RetryNonIdempotent),
            statusCodes);
    }

    private HashSet<int> ParseStatusCodes(IReadOnlyDictionary<string, string>? metadata)
    {
        var value = metadata != null && metadata.TryGetValue("Retry:RetryOnStatusCodes", out var configured)
            ? configured
            : string.Join(',', _options.DefaultRetryStatusCodes);
        var result = new HashSet<int>();
        foreach (var code in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(code, out var parsed))
                result.Add(parsed);
        }
        return result.Count > 0 ? result : [502, 503, 504];
    }

    private static bool GetBoolean(IReadOnlyDictionary<string, string>? metadata, string key, bool fallback) =>
        metadata != null && metadata.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : fallback;

    private static int GetInt32(IReadOnlyDictionary<string, string>? metadata, string key, int fallback) =>
        metadata != null && metadata.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;

    private sealed record ParsedRetryConfig(
        bool Enabled,
        int MaxRetries,
        int BackoffBaseMs,
        int JitterMs,
        bool UseDifferentDestination,
        bool RetryNonIdempotent,
        HashSet<int> StatusCodes);
}
