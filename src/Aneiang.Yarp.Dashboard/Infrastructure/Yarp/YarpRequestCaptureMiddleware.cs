using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Yarp;

/// <summary>
/// Captures incoming proxy request/response details before YARP processes the request.
/// Skips Dashboard requests. Buffers body for parameter capture.
/// Supports structured logging, sampling, filtering, and sanitization.
/// Zero dependency on logging frameworks.
/// Optimized with ArrayPool buffer reuse and minimal allocations.
/// </summary>
public sealed class YarpRequestCaptureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IProxyLogStore _store;
    private readonly LogSanitizer _sanitizer;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly LockFreeStatistics _statistics;
    private readonly string _dashPrefix;
    private readonly bool _loggingEnabled;
    private readonly bool _enableSampling;
    private readonly double _samplingRate;
    private readonly bool _logErrorsOnly;
    private readonly bool _enableRequestBodyCapture;
    private readonly bool _enableResponseBodyCapture;
    private readonly int _maxBodyBufferBytes;
    private readonly int _minLogLevelNumeric;

    // Cached collections for fast filtering (avoid property access)
    private readonly HashSet<string>? _routeWhitelist;
    private readonly HashSet<string>? _routeBlacklist;

    // (Removed _minLogLevel + _minLogLevelChecked — replaced by pre-computed _minLogLevelNumeric)

    // Reusable Random instance per thread to avoid contention
    private static readonly ThreadLocal<Random> _threadRandom = new(() => new Random());

    /// <summary>
    /// Static file extensions to skip from logging (frontend resources).
    /// </summary>
    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".css", ".map",
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".webp", ".bmp", ".avif",
        ".woff", ".woff2", ".ttf", ".eot", ".otf",
        ".mp3", ".mp4", ".wav", ".avi", ".webm", ".ogg",
        ".pdf", ".zip", ".gz", ".tar", ".rar",
        ".html", ".htm", ".xml", ".txt"
    };

    /// <summary>
    /// Content root path for the Dashboard static files. Used to skip logging for frontend resources.
    /// </summary>
    private const string ContentRoot = "/_content/Aneiang.Yarp.Dashboard";


    /// <summary>
    /// Creates the middleware.
    /// Pre-computes and caches all configuration values for optimal performance.
    /// </summary>
    public YarpRequestCaptureMiddleware(
        RequestDelegate next,
        IProxyLogStore store,
        LogSanitizer sanitizer,
        RecyclableMemoryStreamManager memoryStreamManager,
        LockFreeStatistics statistics,
        IOptions<DashboardOptions> options)
    {
        _next = next;
        _store = store;
        _sanitizer = sanitizer;
        _memoryStreamManager = memoryStreamManager;
        _statistics = statistics;

        var opt = options.Value;
        _dashPrefix = "/" + opt.RoutePrefix.Trim('/');
        _loggingEnabled = opt.EnableProxyLogging;

        // Pre-cache all frequently accessed configuration values
        _enableSampling = opt.EnableLogSampling;
        _samplingRate = opt.LogSamplingRate;
        _logErrorsOnly = opt.LogErrorsOnly;
        _enableRequestBodyCapture = opt.EnableProxyRequestBodyCapture;
        _enableResponseBodyCapture = opt.EnableProxyResponseBodyCapture;
        _maxBodyBufferBytes = Math.Max(0, opt.LogMaxBodyBufferBytes);

        // Pre-convert MinLogLevel string to numeric value for fast comparison
        // Bug fix: was hardcoded to 0 (Debug), making DashboardOptions.MinLogLevel ineffective
        _minLogLevelNumeric = opt.MinLogLevel switch
        {
            "Critical" => 4,
            "Error" => 3,
            "Warning" => 2,
            "Information" => 1,
            _ => 0 // Debug/Verbose — capture all
        };

        // Pre-convert route lists to HashSets for O(1) lookup
        if (opt.LogRouteWhitelist?.Count > 0)
            _routeWhitelist = new HashSet<string>(opt.LogRouteWhitelist, StringComparer.OrdinalIgnoreCase);
        if (opt.LogRouteBlacklist?.Count > 0)
            _routeBlacklist = new HashSet<string>(opt.LogRouteBlacklist, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Captures request/response info. Skips Dashboard paths.
    /// 
    /// ⚠️ P0 Bug Fix (v2.4): ProxyRequest LogEntry is now added AFTER ShouldLog check,
    /// not before. Previously, ProxyRequest was written before ShouldLog, causing:
    /// - Sampling filter was ineffective (filtered requests still had ProxyRequest entries)
    /// - MinLogLevel filter was ineffective
    /// - "Orphan" ProxyRequest entries without matching ProxyResponse accumulated in memory
    /// 
    /// Trade-off: ProxyRequest no longer appears immediately for slow backends.
    /// Both entries are added only after ShouldLog passes, ensuring memory correctness.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_loggingEnabled)
        {
            await _next(context);
            return;
        }

        // Skip Dashboard requests
        if (context.Request.Path.StartsWithSegments(_dashPrefix, StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments(ContentRoot, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Skip gRPC requests — response body capture replaces IHttpResponseBodyFeature
        // which breaks HTTP/2 trailer support required by gRPC (grpc-status, etc.).
        if (IsGrpcRequest(context.Request))
        {
            await _next(context);
            return;
        }

        // Skip frontend static resource requests
        var extension = Path.GetExtension(context.Request.Path.Value);
        if (extension != null && SkippedExtensions.Contains(extension))
        {
            await _next(context);
            return;
        }

        // ── Phase 1: Capture request data (before _next, but DON'T add to store yet) ──
        var timestamp = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();
        // TraceId deferred to Phase 5 (after ShouldLog) — avoids allocation when request is filtered out
        // Reuse UpstreamPath string for both ProxyRequest and ProxyResponse (eliminates duplicate concatenation)
        var upstreamPath = context.Request.Path + context.Request.QueryString.Value;

        var captureRequestBody = _enableRequestBodyCapture && IsRequestBodyCaptureSafe(context.Request);
        var requestBody = string.Empty;
        if (captureRequestBody)
        {
            context.Request.EnableBuffering();
            requestBody = await ReadBodyAsync(context.Request, _maxBodyBufferBytes);
        }

        var sanitizedRequestBody = _sanitizer.SanitizeJsonBody(requestBody);
        var requestText = _sanitizer.TruncateText(sanitizedRequestBody, out var requestTruncated);
        var requestHeaders = _sanitizer.SanitizeHeaders(context.Request.Headers);

        // Set up TeeResponseCaptureStream before _next (still needed for response capture)
        var captureResponseBody = _enableResponseBodyCapture && IsResponseBodyCaptureCandidate(context.Request);
        var originalResponseBody = context.Response.Body;
        var originalBodyFeature = context.Features.Get<IHttpResponseBodyFeature>();
        TeeResponseCaptureStream? responseBodyStream = null;

        if (captureResponseBody && originalBodyFeature != null && _maxBodyBufferBytes > 0)
        {
            responseBodyStream = new TeeResponseCaptureStream(originalResponseBody, _maxBodyBufferBytes, _memoryStreamManager);
            var captureFeature = new StreamResponseBodyFeature(responseBodyStream, originalBodyFeature);
            context.Response.Body = responseBodyStream;
            context.Features.Set<IHttpResponseBodyFeature>(captureFeature);
        }

        // ── Phase 2: Process request through YARP pipeline ──
        await _next(context);
        stopwatch.Stop();

        // ── Phase 3: ShouldLog check — ONLY add entries if this passes ──
        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var downstreamUrl = BuildDownstreamUrl(proxyFeature, context.Request);
        var routeId = proxyFeature?.Route?.Config?.RouteId;
        var clusterId = proxyFeature?.Route?.Config?.ClusterId;

        // ShouldLog now determines whether BOTH ProxyRequest and ProxyResponse are added
        if (!ShouldLog(context, routeId))
        {
            if (responseBodyStream != null)
            {
                RestoreResponseStream(originalResponseBody, originalBodyFeature!, context);
                await responseBodyStream.DisposeAsync();
            }
            return; // No entries added at all — sampling/filtering is now truly effective
        }

        // ── Phase 4: Process response data ──
        var responseBodyText = responseBodyStream != null && IsResponseBodyCaptureSafe(context.Response)
            ? await ReadStreamAsync(responseBodyStream.CapturedBody, _maxBodyBufferBytes)
            : string.Empty;

        var downstreamBody = GetDownstreamBody(context);
        var downstreamMethod = GetDownstreamMethod(context);
        var downstreamUrlCaptured = GetDownstreamUrl(context) ?? downstreamUrl;

        if (responseBodyStream != null)
        {
            RestoreResponseStream(originalResponseBody, originalBodyFeature!, context);
            await responseBodyStream.DisposeAsync();
        }

        // Sanitize and truncate downstream body
        string? downstreamText = null;
        var downstreamTruncatedFlag = false;
        if (downstreamBody != null)
        {
            var sanitizedDownstreamBody = _sanitizer.SanitizeJsonBody(downstreamBody);
            downstreamText = _sanitizer.TruncateText(sanitizedDownstreamBody, out downstreamTruncatedFlag);
        }

        // Sanitize and truncate response body
        var sanitizedResponseBody = _sanitizer.SanitizeJsonBody(responseBodyText);
        var responseText = _sanitizer.TruncateText(sanitizedResponseBody, out var responseTruncated);
        var responseHeaders = _sanitizer.SanitizeHeaders(context.Response.Headers);

        // ── Phase 5: Add BOTH entries to store (after ShouldLog passes) ──

        // Compute TraceId only after ShouldLog passes — saves allocation for filtered requests
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        // Add ProxyRequest (moved from before _next to after ShouldLog — P0 fix)
        // Message = null: frontend derives "[Request] GET /path" from EventType+Method+UpstreamPath
        // Saves ~50-100 bytes per LogEntry (redundant string elimination)
        _store.Add(new LogEntry
        {
            Timestamp = timestamp,
            EventType = LogEventType.ProxyRequest,
            Level = "Information",
            Message = null,
            TraceId = traceId,
            Method = context.Request.Method,
            UpstreamPath = upstreamPath,
            RequestHeaders = requestHeaders,
            RequestBody = requestText,
            RequestBodyTruncated = requestTruncated
        });

        // Add ProxyResponse with route/cluster/downstream info
        // Message = null: frontend derives "[Response] 200 GET /path" from EventType+StatusCode+Method+UpstreamPath
        _store.Add(new LogEntry
        {
            Timestamp = DateTime.Now,
            EventType = LogEventType.ProxyResponse,
            Level = GetLogLevel(context.Response.StatusCode),
            Message = null,
            TraceId = traceId,
            RouteId = routeId,
            ClusterId = clusterId,
            Method = context.Request.Method,
            UpstreamPath = upstreamPath,
            DownstreamUrl = downstreamUrlCaptured,
            DownstreamMethod = downstreamMethod,
            DownstreamBody = downstreamText,
            DownstreamBodyTruncated = downstreamTruncatedFlag,
            StatusCode = context.Response.StatusCode,
            ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
            ResponseHeaders = responseHeaders,
            ResponseBody = responseText,
            ResponseBodyTruncated = responseTruncated
        });

        // ── Phase 6: Record to lock-free statistics accumulator (zero-allocation hot path) ──
        _statistics.RecordRequest(
            context.Response.StatusCode,
            (long)(stopwatch.Elapsed.TotalMilliseconds * 1000), // Convert ms → micros
            routeId != null ? JitOptimizedHotPaths.FastStringHash(routeId) : 0,
            clusterId != null ? JitOptimizedHotPaths.FastStringHash(clusterId) : 0);
    }

    /// <summary>
    /// Restores the original response stream.
    /// </summary>
    private static void RestoreResponseStream(
        Stream originalResponseBody,
        IHttpResponseBodyFeature originalBodyFeature,
        HttpContext context)
    {
        context.Response.Body = originalResponseBody;
        context.Features.Set(originalBodyFeature);
    }

    /// <summary>
    /// Determines if a request should be logged based on sampling and filtering rules.
    /// Uses cached configuration values for optimal performance.
    /// </summary>
    private bool ShouldLog(HttpContext context, string? routeId)
    {
        // Fast path: check minimum log level first (cheapest check)
        // Bug fix: _minLogLevel was always 0 (Debug), DashboardOptions.MinLogLevel was ignored
        // Now uses pre-computed _minLogLevelNumeric from configuration
        var currentLevel = context.Response.StatusCode switch
        {
            >= 500 => 3,  // Error
            >= 400 => 2,  // Warning
            _ => 1        // Information
        };
        if (currentLevel < _minLogLevelNumeric)
        {
            return false;
        }

        // Check errors only mode (cached value)
        if (_logErrorsOnly && context.Response.StatusCode < 400)
        {
            return false;
        }

        // Check route whitelist (cached HashSet)
        if (_routeWhitelist != null)
        {
            if (string.IsNullOrEmpty(routeId) || !_routeWhitelist.Contains(routeId))
            {
                return false;
            }
        }

        // Check route blacklist (cached HashSet)
        if (_routeBlacklist != null && !string.IsNullOrEmpty(routeId) && _routeBlacklist.Contains(routeId))
        {
            return false;
        }

        // Check sampling (cached values, thread-local Random)
        if (_enableSampling && _samplingRate < 1.0)
        {
            if (_threadRandom.Value!.NextDouble() > _samplingRate)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets log level based on HTTP status code.
    /// </summary>
    private static string GetLogLevel(int statusCode)
    {
        return statusCode switch
        {
            >= 500 => "Error",
            >= 400 => "Warning",
            _ => "Information"
        };
    }

    private static string? GetDownstreamBody(HttpContext context)
    {
        if (context.Items.TryGetValue("DownstreamBody", out var obj) && obj is byte[] bodyBytes && bodyBytes.Length > 0)
        {
            return Encoding.UTF8.GetString(bodyBytes);
        }
        return null;
    }

    private static string? GetDownstreamMethod(HttpContext context)
    {
        if (context.Items.TryGetValue("DownstreamMethod", out var obj) && obj is string method)
        {
            return method;
        }
        return null;
    }

    private static string? GetDownstreamUrl(HttpContext context)
    {
        if (context.Items.TryGetValue("DownstreamUrl", out var obj) && obj is string url)
        {
            return url;
        }
        return null;
    }

    private static bool IsRequestBodyCaptureSafe(HttpRequest request)
    {
        if (request.ContentLength is null or 0)
            return false;

        if (request.ContentLength > 0 && IsStreamingRequest(request))
            return false;

        return request.HasJsonContentType() || IsTextLikeContentType(request.ContentType);
    }

    private static bool IsResponseBodyCaptureCandidate(HttpRequest request)
    {
        // Skip TeeStream setup for requests with known binary content types (uploads etc.)
        // null ContentType (e.g. GET requests) is allowed — response may still be text-like
        if (request.ContentType != null && !IsTextLikeContentType(request.ContentType))
            return false;
        return !IsStreamingRequest(request) && !request.Headers.ContainsKey("Range");
    }

    private static bool IsResponseBodyCaptureSafe(HttpResponse response)
    {
        return !IsTextEventStream(response.ContentType) && IsTextLikeContentType(response.ContentType);
    }

    private static bool IsStreamingRequest(HttpRequest request)
    {
        return request.Headers.Connection.Any(v => v != null && v.Contains("Upgrade", StringComparison.OrdinalIgnoreCase)) ||
               request.Headers.Accept.Any(v => v != null && v.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTextEventStream(string? contentType)
    {
        return contentType != null && contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextLikeContentType(string? contentType)
    {
        return contentType != null &&
               (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("form", StringComparison.OrdinalIgnoreCase));
    }

    private static string? BuildDownstreamUrl(IReverseProxyFeature? proxy, HttpRequest request)
    {
        if (proxy?.ProxiedDestination?.Model?.Config?.Address is not { } baseUrl)
            return null;

        baseUrl = baseUrl.TrimEnd('/');
        var originalPath = request.Path.Value ?? "/";
        var downstreamPath = originalPath;
        var matchPath = proxy.Route?.Config?.Match?.Path;

        // Extract catch-all value from the match pattern
        var catchAllValue = ExtractCatchAllValue(originalPath, matchPath);

        // Apply transforms in order
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

    /// <summary>
    /// Detects gRPC requests by content type.
    /// gRPC requests use <c>application/grpc</c> (optionally with <c>+proto</c> or <c>+json</c>
    /// encoding suffix) and require HTTP/2 trailer support which is broken by response body capture.
    /// Also matches gRPC-Web (<c>application/grpc-web</c>).
    /// </summary>
    private static bool IsGrpcRequest(HttpRequest request)
    {
        return request.ContentType != null &&
               request.ContentType.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads request body with truncation support using ArrayPool for reduced allocations.
    /// Memory optimization (v2.4): Replaces StreamReader.ReadToEndAsync() with ArrayPool-based
    /// reading to avoid internal char[] buffer allocation per request.
    /// </summary>
    private static async Task<string> ReadBodyAsync(HttpRequest request, int maxBodyBytes)
    {
        if (request.ContentLength == null || request.ContentLength == 0 || maxBodyBytes <= 0)
            return string.Empty;

        if (request.ContentLength > maxBodyBytes)
            return $"[{request.ContentType}] ({request.ContentLength} bytes) - too large to log";

        request.Body.Seek(0, SeekOrigin.Begin);
        var buffer = ArrayPool<char>.Shared.Rent(maxBodyBytes);
        try
        {
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var read = await reader.ReadAsync(buffer, 0, maxBodyBytes);
            request.Body.Seek(0, SeekOrigin.Begin);
            return new string(buffer, 0, read);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Reads a stream with truncation support using ArrayPool for reduced allocations.
    /// Memory optimization (v2.4): Truncation path avoids double string allocation
    /// by appending truncation marker directly in the pooled buffer before creating the final string.
    /// </summary>
    private static async Task<string> ReadStreamAsync(Stream stream, int maxBodyBytes)
    {
        if (maxBodyBytes <= 0)
            return string.Empty;

        stream.Seek(0, SeekOrigin.Begin);

        if (stream.Length > maxBodyBytes)
        {
            var buffer = ArrayPool<char>.Shared.Rent(maxBodyBytes + 30); // +30 for truncation marker
            try
            {
                using var reader = new StreamReader(stream, leaveOpen: true);
                var read = await reader.ReadAsync(buffer, 0, maxBodyBytes);
                // Append truncation marker directly in buffer — avoids double string allocation
                var truncateMarker = "\n... [TRUNCATED - response too large]";
                for (int i = 0; i < truncateMarker.Length && read + i < buffer.Length; i++)
                    buffer[read + i] = truncateMarker[i];
                return new string(buffer, 0, read + Math.Min(truncateMarker.Length, buffer.Length - read));
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        using var readerFull = new StreamReader(stream, leaveOpen: true);
        return await readerFull.ReadToEndAsync();
    }

    /// <summary>
    /// TeeStream captures response body while forwarding to the original response stream.
    /// Memory optimization (v2.4): Uses RecyclableMemoryStreamManager instead of MemoryStream
    /// to eliminate LOH fragmentation from repeated 64KB allocations.
    /// </summary>
    private sealed class TeeResponseCaptureStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _limitBytes;
        private int _capturedBytes;

        public TeeResponseCaptureStream(Stream inner, int limitBytes, RecyclableMemoryStreamManager manager)
        {
            _inner = inner;
            _limitBytes = Math.Max(0, limitBytes);
            // RecyclableMemoryStream pools the underlying byte[] — no LOH fragmentation
            CapturedBody = manager.GetStream("TeeResponseCapture", Math.Min(_limitBytes, 64 * 1024));
        }

        public Stream CapturedBody { get; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            Capture(buffer.AsSpan(offset, count));
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Capture(buffer);
            _inner.Write(buffer);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Capture(buffer.Span);
            await _inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Capture(buffer.AsSpan(offset, count));
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        private void Capture(ReadOnlySpan<byte> buffer)
        {
            if (_capturedBytes >= _limitBytes || buffer.Length == 0)
                return;

            var remaining = _limitBytes - _capturedBytes;
            var bytesToCapture = Math.Min(remaining, buffer.Length);
            CapturedBody.Write(buffer[..bytesToCapture]);
            _capturedBytes += bytesToCapture;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CapturedBody.Dispose(); // Returns underlying byte[] to RecyclableMemoryStreamManager pool
            }
            base.Dispose(disposing);
        }
    }
}
