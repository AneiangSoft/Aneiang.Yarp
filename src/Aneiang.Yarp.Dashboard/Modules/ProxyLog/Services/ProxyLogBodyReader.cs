using System.Buffers;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IO;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Stateless utility methods for reading and buffering request/response bodies.
/// Extracted from YarpRequestCaptureMiddleware to reduce its responsibilities.
/// </summary>
public static class ProxyLogBodyReader
{
    /// <summary>
    /// Determines if request body capture is safe (text-like, not streaming, not too large).
    /// </summary>
    public static bool IsRequestBodyCaptureSafe(HttpRequest request)
    {
        if (request.ContentLength is null or 0)
            return false;

        if (request.ContentLength > 0 && IsStreamingRequest(request))
            return false;

        return request.HasJsonContentType() || IsTextLikeContentType(request.ContentType);
    }

    /// <summary>
    /// Determines if response body capture via TeeStream is a candidate
    /// (not binary content type, not streaming, not range request).
    /// </summary>
    public static bool IsResponseBodyCaptureCandidate(HttpRequest request)
    {
        if (request.ContentType != null && !IsTextLikeContentType(request.ContentType))
            return false;
        return !IsStreamingRequest(request) && !request.Headers.ContainsKey("Range");
    }

    /// <summary>
    /// Determines if response body capture is safe (not SSE, text-like).
    /// </summary>
    public static bool IsResponseBodyCaptureSafe(HttpResponse response)
    {
        return !IsTextEventStream(response.ContentType) && IsTextLikeContentType(response.ContentType);
    }

    /// <summary>
    /// Reads request body with truncation using ArrayPool for reduced allocations.
    /// </summary>
    public static async Task<string> ReadRequestBodyAsync(HttpRequest request, int maxBodyBytes)
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
    /// Reads a captured response stream with truncation support using ArrayPool.
    /// </summary>
    public static async Task<string> ReadStreamAsync(Stream stream, int maxBodyBytes)
    {
        if (maxBodyBytes <= 0)
            return string.Empty;

        stream.Seek(0, SeekOrigin.Begin);

        if (stream.Length > maxBodyBytes)
        {
            var buffer = ArrayPool<char>.Shared.Rent(maxBodyBytes + 30);
            try
            {
                using var reader = new StreamReader(stream, leaveOpen: true);
                var read = await reader.ReadAsync(buffer, 0, maxBodyBytes);
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
    /// Gets downstream body from HttpContext.Items (set by DownstreamCaptureTransformProvider).
    /// </summary>
    public static string? GetDownstreamBody(HttpContext context)
    {
        if (context.Items.TryGetValue("DownstreamBody", out var obj) && obj is byte[] bodyBytes && bodyBytes.Length > 0)
            return Encoding.UTF8.GetString(bodyBytes);
        return null;
    }

    /// <summary>Gets downstream method from HttpContext.Items.</summary>
    public static string? GetDownstreamMethod(HttpContext context)
    {
        if (context.Items.TryGetValue("DownstreamMethod", out var obj) && obj is string method)
            return method;
        return null;
    }

    /// <summary>Gets downstream URL from HttpContext.Items.</summary>
    public static string? GetDownstreamUrl(HttpContext context)
    {
        if (context.Items.TryGetValue("DownstreamUrl", out var obj) && obj is string url)
            return url;
        return null;
    }

    /// <summary>
    /// Creates a TeeResponseCaptureStream and replaces the response body stream
    /// for response body capture. Returns null if capture is not applicable.
    /// </summary>
    public static TeeResponseCaptureStream? SetupResponseCapture(
        HttpContext context,
        int maxBodyBufferBytes,
        RecyclableMemoryStreamManager memoryStreamManager,
        out Stream? originalBody,
        out IHttpResponseBodyFeature? originalBodyFeature)
    {
        originalBody = context.Response.Body;
        originalBodyFeature = context.Features.Get<IHttpResponseBodyFeature>();

        if (originalBodyFeature == null || maxBodyBufferBytes <= 0)
            return null;

        if (!IsResponseBodyCaptureCandidate(context.Request))
            return null;

        var teeStream = new TeeResponseCaptureStream(originalBody, maxBodyBufferBytes, memoryStreamManager);
        var captureFeature = new StreamResponseBodyFeature(teeStream, originalBodyFeature);
        context.Response.Body = teeStream;
        context.Features.Set<IHttpResponseBodyFeature>(captureFeature);
        return teeStream;
    }

    /// <summary>Restores the original response stream after capture.</summary>
    public static void RestoreResponseStream(
        Stream originalBody,
        IHttpResponseBodyFeature originalBodyFeature,
        HttpContext context)
    {
        context.Response.Body = originalBody;
        context.Features.Set(originalBodyFeature);
    }

    /// <summary>Gets log level string based on HTTP status code.</summary>
    public static string GetLogLevel(int statusCode)
    {
        return statusCode switch
        {
            >= 500 => "Error",
            >= 400 => "Warning",
            _ => "Information"
        };
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
}
