using Aneiang.Yarp.Plugins;
using Aneiang.Yarp.Plugin.ProxyLog.Services;
using Aneiang.Yarp.Plugin.ProxyLog.Models;
using Aneiang.Yarp.Infrastructure.Performance;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using System.Diagnostics;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Plugin.ProxyLog;

/// <summary>
/// Captures incoming proxy request/response details before YARP processes the request.
/// Thin orchestrator that delegates to:
/// - <see cref="ILogFilter"/> for skip/filter/sampling decisions
/// - <see cref="ProxyLogBodyReader"/> for body buffering and content-type checks
/// - <see cref="IProxyLogCapture"/> for log entry construction and storage
/// - <see cref="LockFreeStatistics"/> for real-time statistics
/// </summary>
public sealed class YarpRequestCaptureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogFilter _filter;
    private readonly IProxyLogCapture _capture;
    private readonly ILogSanitizer _sanitizer;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly LockFreeStatistics _statistics;
    private readonly ProxyLogRuntimeSettings _runtimeSettings;
    private readonly GatewayPluginExecutionPlanProvider _executionPlans;

    public YarpRequestCaptureMiddleware(
        RequestDelegate next,
        ILogFilter filter,
        IProxyLogCapture capture,
        ILogSanitizer sanitizer,
        RecyclableMemoryStreamManager memoryStreamManager,
        LockFreeStatistics statistics,
        ProxyLogRuntimeSettings runtimeSettings,
        GatewayPluginExecutionPlanProvider executionPlans)
    {
        _next = next;
        _filter = filter;
        _capture = capture;
        _sanitizer = sanitizer;
        _memoryStreamManager = memoryStreamManager;
        _statistics = statistics;
        _runtimeSettings = runtimeSettings;
        _executionPlans = executionPlans;
    }

    /// <summary>
    /// Captures request/response info. Skips Dashboard paths, gRPC, static files.
    /// Log entries are added only after ShouldLog passes (P0 fix: sampling/filtering effective).
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // RouteModel endpoint metadata is populated by endpoint routing before the YARP proxy pipeline runs.
        var routeId = context.GetEndpoint()?.Metadata.GetMetadata<RouteModel>()?.Config.RouteId;
        var defaults = _runtimeSettings.Current;
        if (!RouteProxyLogSettingsResolver.TryResolve(
                _executionPlans.Current, routeId, defaults, out var settings) ||
            _filter.IsSkippedRequest(context) ||
            (settings.SamplingEnabled && Random.Shared.NextDouble() > settings.SamplingRate))
        {
            await _next(context);
            return;
        }

        // ── Phase 1: Capture request data (before _next) ──
        var timestamp = DateTime.Now;
        var startTimestamp = Stopwatch.GetTimestamp();
        var upstreamPath = context.Request.Path + context.Request.QueryString.Value;

        var captureRequestBody = settings.RequestBodyCaptureEnabled && ProxyLogBodyReader.IsRequestBodyCaptureSafe(context.Request);
        var requestBody = captureRequestBody
            ? await ProxyLogBodyReader.ReadRequestBodyAsync(context.Request, settings.MaxBodyBufferBytes)
            : string.Empty;

        var sanitizedRequestBody = _sanitizer.SanitizeBody(requestBody, context.Request.ContentType);
        var requestText = TruncateText(sanitizedRequestBody, settings.MaxBodyLength, out var requestTruncated);

        // ── Phase 2: Set up response body capture ──
        TeeResponseCaptureStream? responseBodyStream = null;
        Stream? originalBody = null;
        Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature? originalBodyFeature = null;

        if (settings.ResponseBodyCaptureEnabled)
        {
            responseBodyStream = ProxyLogBodyReader.SetupResponseCapture(
                context, settings.MaxBodyBufferBytes, _memoryStreamManager,
                out originalBody, out originalBodyFeature);
        }

        try
        {
            // ── Phase 3: Process request through YARP pipeline ──
            await _next(context);
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

            // ── Phase 4: ShouldLog check ──
            var proxyFeature = context.Features.Get<IReverseProxyFeature>();
            routeId ??= proxyFeature?.Route?.Config?.RouteId;
            var clusterId = proxyFeature?.Route?.Config?.ClusterId;

            if (settings.ErrorsOnly && context.Response.StatusCode < 400)
                return;

            // ── Phase 5: Process response data ──
            var responseBodyText = responseBodyStream != null && ProxyLogBodyReader.IsResponseBodyCaptureSafe(context.Response)
                ? await ProxyLogBodyReader.ReadStreamAsync(responseBodyStream.CapturedBody, settings.MaxBodyBufferBytes)
                : string.Empty;

            // The current pipeline does not mutate request bodies after this middleware.
            // Reuse the captured body instead of buffering the YARP HttpContent a second time.
            var downstreamText = requestText;
            var downstreamTruncated = requestTruncated;

        var sanitizedResponseBody = _sanitizer.SanitizeBody(responseBodyText, context.Response.ContentType);
        var responseText = TruncateText(sanitizedResponseBody, settings.MaxBodyLength, out var responseTruncated);
        var requestHeaders = settings.CaptureRequestHeaders
            ? _sanitizer.SanitizeHeaders(context.Request.Headers)
            : new HeaderList();
        var responseHeaders = settings.CaptureResponseHeaders
            ? _sanitizer.SanitizeHeaders(context.Response.Headers)
            : new HeaderList();


            // ── Phase 6: Build and store log entries ──
            _capture.CaptureLogEntry(
                context, proxyFeature, upstreamPath, routeId, clusterId,
                timestamp, elapsed,
                requestHeaders, requestText ?? string.Empty, requestTruncated,
                responseText, responseTruncated, responseHeaders,
                downstreamText, downstreamTruncated);

            // ── Phase 7: Record statistics (zero-allocation hot path) ──
            _statistics.RecordRequest(
                context.Response.StatusCode,
                (long)(elapsed.TotalMilliseconds * 1000),
                routeId != null ? routeId.GetHashCode() : 0,
                clusterId != null ? clusterId.GetHashCode() : 0);
        }
        finally
        {
            if (responseBodyStream != null)
            {
                ProxyLogBodyReader.RestoreResponseStream(originalBody!, originalBodyFeature!, context);
                await responseBodyStream.DisposeAsync();
            }
        }
    }

    private static string? TruncateText(string? text, int maxLength, out bool truncated)
    {
        truncated = false;
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        truncated = true;
        return text[..maxLength] + "\n... [TRUNCATED]";
    }
}