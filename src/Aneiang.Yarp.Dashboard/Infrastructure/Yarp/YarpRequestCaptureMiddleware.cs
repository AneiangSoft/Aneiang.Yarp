using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using System.Diagnostics;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Yarp;

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
    private readonly LogSanitizer _sanitizer;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly LockFreeStatistics _statistics;
    private readonly bool _enableRequestBodyCapture;
    private readonly bool _enableResponseBodyCapture;
    private readonly int _maxBodyBufferBytes;

    public YarpRequestCaptureMiddleware(
        RequestDelegate next,
        ILogFilter filter,
        IProxyLogCapture capture,
        LogSanitizer sanitizer,
        RecyclableMemoryStreamManager memoryStreamManager,
        LockFreeStatistics statistics,
        IOptions<DashboardOptions> options)
    {
        _next = next;
        _filter = filter;
        _capture = capture;
        _sanitizer = sanitizer;
        _memoryStreamManager = memoryStreamManager;
        _statistics = statistics;

        var opt = options.Value;
        _enableRequestBodyCapture = opt.EnableProxyRequestBodyCapture;
        _enableResponseBodyCapture = opt.EnableProxyResponseBodyCapture;
        _maxBodyBufferBytes = Math.Max(0, opt.LogMaxBodyBufferBytes);
    }

    /// <summary>
    /// Captures request/response info. Skips Dashboard paths, gRPC, static files.
    /// Log entries are added only after ShouldLog passes (P0 fix: sampling/filtering effective).
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_filter.IsLoggingEnabled)
        {
            await _next(context);
            return;
        }

        if (_filter.IsSkippedRequest(context))
        {
            await _next(context);
            return;
        }

        // ── Phase 1: Capture request data (before _next) ──
        var timestamp = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();
        var upstreamPath = context.Request.Path + context.Request.QueryString.Value;

        var captureRequestBody = _enableRequestBodyCapture && ProxyLogBodyReader.IsRequestBodyCaptureSafe(context.Request);
        var requestBody = captureRequestBody
            ? await ProxyLogBodyReader.ReadRequestBodyAsync(context.Request, _maxBodyBufferBytes)
            : string.Empty;

        var sanitizedRequestBody = _sanitizer.SanitizeJsonBody(requestBody);
        var requestText = _sanitizer.TruncateText(sanitizedRequestBody, out var requestTruncated);
        var requestHeaders = _sanitizer.SanitizeHeaders(context.Request.Headers);

        // ── Phase 2: Set up response body capture ──
        TeeResponseCaptureStream? responseBodyStream = null;
        Stream? originalBody = null;
        Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature? originalBodyFeature = null;

        if (_enableResponseBodyCapture)
        {
            responseBodyStream = ProxyLogBodyReader.SetupResponseCapture(
                context, _maxBodyBufferBytes, _memoryStreamManager,
                out originalBody, out originalBodyFeature);
        }

        // ── Phase 3: Process request through YARP pipeline ──
        await _next(context);
        stopwatch.Stop();

        // ── Phase 4: ShouldLog check ──
        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var routeId = proxyFeature?.Route?.Config?.RouteId;
        var clusterId = proxyFeature?.Route?.Config?.ClusterId;

        if (!_filter.ShouldLog(context, routeId))
        {
            if (responseBodyStream != null)
            {
                ProxyLogBodyReader.RestoreResponseStream(originalBody!, originalBodyFeature!, context);
                await responseBodyStream.DisposeAsync();
            }
            return;
        }

        // ── Phase 5: Process response data ──
        var responseBodyText = responseBodyStream != null && ProxyLogBodyReader.IsResponseBodyCaptureSafe(context.Response)
            ? await ProxyLogBodyReader.ReadStreamAsync(responseBodyStream.CapturedBody, _maxBodyBufferBytes)
            : string.Empty;

        var downstreamBody = ProxyLogBodyReader.GetDownstreamBody(context);
        string? downstreamText = null;
        var downstreamTruncated = false;
        if (downstreamBody != null)
        {
            var sanitizedDownstreamBody = _sanitizer.SanitizeJsonBody(downstreamBody);
            downstreamText = _sanitizer.TruncateText(sanitizedDownstreamBody, out downstreamTruncated);
        }

        var sanitizedResponseBody = _sanitizer.SanitizeJsonBody(responseBodyText);
        var responseText = _sanitizer.TruncateText(sanitizedResponseBody, out var responseTruncated);
        var responseHeaders = _sanitizer.SanitizeHeaders(context.Response.Headers);

        if (responseBodyStream != null)
        {
            ProxyLogBodyReader.RestoreResponseStream(originalBody!, originalBodyFeature!, context);
            await responseBodyStream.DisposeAsync();
        }

        // ── Phase 6: Build and store log entries ──
        _capture.CaptureLogEntry(
            context, proxyFeature, upstreamPath, routeId, clusterId,
            timestamp, stopwatch.Elapsed,
            requestHeaders, requestBody, requestTruncated,
            responseBodyText, responseTruncated, responseHeaders,
            downstreamText, downstreamTruncated);

        // ── Phase 7: Record statistics (zero-allocation hot path) ──
        _statistics.RecordRequest(
            context.Response.StatusCode,
            (long)(stopwatch.Elapsed.TotalMilliseconds * 1000),
            routeId != null ? JitOptimizedHotPaths.FastStringHash(routeId) : 0,
            clusterId != null ? JitOptimizedHotPaths.FastStringHash(clusterId) : 0);
    }
}
