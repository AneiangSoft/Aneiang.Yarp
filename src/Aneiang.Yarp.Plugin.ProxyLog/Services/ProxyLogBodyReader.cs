using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IO;

namespace Aneiang.Yarp.Plugin.ProxyLog.Services;

/// <summary>
/// Stateless utility methods for reading and buffering request/response bodies.
/// Extracted from YarpRequestCaptureMiddleware to reduce its responsibilities.
/// </summary>
public static class ProxyLogBodyReader
{
    /// <summary>
    /// Parses the charset from a Content-Type header value.
    /// Returns null if not found, falling back to UTF-8 for JSON.
    /// </summary>
    private static Encoding? ResolveEncoding(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;

        // Extract charset=xxx parameter
        var span = contentType.AsSpan();
        var charsetIndex = span.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (charsetIndex >= 0)
        {
            var start = charsetIndex + 8; // "charset=".Length
            var rest = span[start..];
            // Find end of charset value (semicolon or end)
            var end = rest.IndexOf(';');
            var charset = end >= 0 ? rest[..end].ToString().Trim().Trim('"', '\'') : rest.ToString().Trim().Trim('"', '\'');
            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch
            {
                // Unknown charset, fall through to defaults
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the appropriate encoding for a content type.
    /// Uses charset from Content-Type if specified, otherwise UTF-8 for JSON/text.
    /// </summary>
    private static Encoding GetEncoding(string? contentType)
    {
        return ResolveEncoding(contentType) ?? Encoding.UTF8;
    }

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
        // The downstream response content type is not available before proxying.
        // Do not use the upstream request content type to decide whether its response can be logged.
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
    /// Automatically decompresses gzip/deflate/br encoded bodies.
    /// </summary>
    public static async Task<string> ReadRequestBodyAsync(HttpRequest request, int maxBodyBytes)
    {
        if (request.ContentLength == null || request.ContentLength == 0 || maxBodyBytes <= 0)
            return string.Empty;

        if (request.ContentLength > maxBodyBytes)
            return $"[{request.ContentType}] ({request.ContentLength} bytes) - too large to log";

        var contentLength = (int)request.ContentLength.Value;
        request.EnableBuffering(bufferThreshold: contentLength, bufferLimit: maxBodyBytes);
        request.Body.Position = 0;
        try
        {
            var encoding = GetEncoding(request.ContentType);
            var encodingHeader = request.Headers["Content-Encoding"].ToString();
            var stream = request.Body;
            if (!string.IsNullOrEmpty(encodingHeader))
            {
                stream = WrapDecompressionStream(request.Body, encodingHeader);
            }
            using var reader = new StreamReader(stream, encoding, leaveOpen: true);
            return await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    /// <summary>
    /// Reads a captured response stream with truncation support using ArrayPool.
    /// Uses the response Content-Type charset for correct decoding.
    /// Automatically decompresses gzip/deflate/br encoded bodies.
    /// </summary>
    public static async Task<string> ReadStreamAsync(Stream stream, int maxBodyBytes, string? contentType = null, string? contentEncoding = null)
    {
        if (maxBodyBytes <= 0)
            return string.Empty;

        stream.Seek(0, SeekOrigin.Begin);
        var encoding = GetEncoding(contentType);

        // Read all bytes first -- we need them as raw bytes for decoding
        var rawBytes = new byte[stream.Length];
        var rawRead = 0;
        while (rawRead < rawBytes.Length)
        {
            var n = await stream.ReadAsync(rawBytes, rawRead, rawBytes.Length - rawRead);
            if (n == 0) break;
            rawRead += n;
        }

        // Try to decompress if Content-Encoding indicates it
        byte[] bodyBytes = rawBytes;
        int bodyLength = rawRead;
        if (!string.IsNullOrEmpty(contentEncoding))
        {
            try
            {
                bodyBytes = DecompressBytes(rawBytes, rawRead, contentEncoding);
                bodyLength = bodyBytes.Length;
            }
            catch
            {
                // Decompression failed -- treat as raw text, may show garbled output
            }
        }

        if (bodyLength > maxBodyBytes)
        {
            var safeLen = TrimIncompleteUtf8(bodyBytes, maxBodyBytes);
            var text = encoding.GetString(bodyBytes, 0, safeLen);
            return text + "\n... [TRUNCATED - response too large]";
        }

        var fullSafeLen = TrimIncompleteUtf8(bodyBytes, bodyLength);
        return encoding.GetString(bodyBytes, 0, fullSafeLen);
    }

    /// <summary>
    /// Wraps a stream with a decompression stream based on the Content-Encoding value.
    /// Supports gzip, deflate, and br (Brotli).
    /// </summary>
    private static Stream WrapDecompressionStream(Stream stream, string contentEncoding)
    {
        var encoding = contentEncoding.Trim().ToLowerInvariant();
        return encoding switch
        {
            "gzip" => new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true),
            "deflate" => new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true),
            "br" => new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: true),
            _ => stream
        };
    }

    /// <summary>
    /// Decompresses a byte buffer using the specified Content-Encoding.
    /// </summary>
    private static byte[] DecompressBytes(byte[] data, int length, string contentEncoding)
    {
        using var input = new MemoryStream(data, 0, length, writable: false);
        using var output = new MemoryStream();
        using (var decompressor = WrapDecompressionStream(input, contentEncoding))
        {
            decompressor.CopyTo(output);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Trims incomplete UTF-8 multi-byte sequences from the end of a byte buffer.
    /// For non-UTF-8 encodings (e.g. GBK), this is a no-op since they use 2-byte fixed-width.
    /// </summary>
    private static int TrimIncompleteUtf8(byte[] buffer, int length)
    {
        if (length == 0) return 0;
        // Scan backwards up to 4 bytes (max UTF-8 sequence length)
        for (var i = 1; i <= Math.Min(4, length); i++)
        {
            var b = buffer[length - i];
            if ((b & 0xC0) != 0x80)
            {
                // This is a leading byte
                var expected = b switch
                {
                    >= 0xF0 => 4,   // 4-byte sequence
                    >= 0xE0 => 3,   // 3-byte sequence
                    >= 0xC0 => 2,   // 2-byte sequence
                    _ => 1           // ASCII / single byte
                };
                // If we don't have enough bytes to complete the sequence, trim it
                if (i < expected)
                    return length - i;
                return length;
            }
        }
        // All bytes are continuation bytes (very unusual) -- trim 1
        return length > 0 ? length - 1 : 0;
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
