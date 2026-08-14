using System.Globalization;
using System.IO.Compression;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Plugin.Compression;

/// <summary>
/// Route-scoped response compression. Buffers the upstream response and rewrites it with
/// Brotli (preferred) or Gzip when the client accepts the encoding, the media type is
/// compressible, and the payload exceeds the configured minimum size.
/// </summary>
public sealed class CompressionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly GatewayPluginExecutionPlanProvider _plans;

    public CompressionMiddleware(RequestDelegate next, GatewayPluginExecutionPlanProvider plans)
    {
        _next = next;
        _plans = plans;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var routeId = context.Features.Get<IReverseProxyFeature>()?.Route?.Config?.RouteId;
        if (string.IsNullOrWhiteSpace(routeId) ||
            !_plans.Current.CompressionByRoute.TryGetValue(routeId, out var config) || !config.Enabled)
        {
            await _next(context);
            return;
        }

        // Negotiate the strongest accepted encoding: Brotli is preferred over Gzip.
        var acceptEncoding = context.Request.Headers.AcceptEncoding.ToString();
        var useBrotli = AcceptsEncoding(acceptEncoding, "br");
        var useGzip = !useBrotli && (AcceptsEncoding(acceptEncoding, "gzip") || AcceptsEncoding(acceptEncoding, "x-gzip"));
        if (!useBrotli && !useGzip)
        {
            await _next(context);
            return;
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);

            var body = buffer.ToArray();
            if (CanCompress(context, config, body.Length))
            {
                await using var compressed = new MemoryStream();
                if (useBrotli)
                {
                    await using (var brotli = new BrotliStream(compressed, ParseLevel(config.CompressionLevel), leaveOpen: true))
                    {
                        await brotli.WriteAsync(body, context.RequestAborted);
                    }
                }
                else
                {
                    await using (var gzip = new GZipStream(compressed, ParseLevel(config.CompressionLevel), leaveOpen: true))
                    {
                        await gzip.WriteAsync(body, context.RequestAborted);
                    }
                }

                // Only ship the compressed representation when it actually shrinks the payload.
                if (compressed.Length > 0 && compressed.Length < body.Length)
                {
                    context.Response.Headers.ContentEncoding = useBrotli ? "br" : "gzip";
                    AppendVaryAcceptEncoding(context.Response.Headers);
                    context.Response.Headers.ContentLength = null; // original length is no longer valid
                    compressed.Position = 0;
                    await compressed.CopyToAsync(original, context.RequestAborted);
                    return;
                }
            }

            // Not compressed: replay the original payload with its exact length.
            context.Response.Headers.ContentLength = body.Length;
            buffer.Position = 0;
            await buffer.CopyToAsync(original, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = original;
        }
    }

    private static bool CanCompress(HttpContext context, CompressionExecutionConfig config, int bodyLength) =>
        context.Response.StatusCode is >= 200 and <= 299 &&
        bodyLength >= config.MinResponseSize &&
        bodyLength > 0 &&
        string.IsNullOrEmpty(context.Response.Headers.ContentEncoding) &&
        !context.Request.Headers.ContainsKey("Range") &&
        MediaTypesMatch(context.Response.ContentType, config.MimeTypes);

    private static bool MediaTypesMatch(string? contentType, string[] configuredTypes)
    {
        if (string.IsNullOrEmpty(contentType) || configuredTypes.Length == 0) return false;
        var mediaType = contentType.AsSpan(0, contentType.IndexOf(';') is var end && end >= 0 ? end : contentType.Length).Trim().ToString();
        if (mediaType.Length == 0) return false;
        foreach (var candidate in configuredTypes)
        {
            if (string.Equals(candidate, mediaType, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static CompressionLevel ParseLevel(string? level) => level switch
    {
        "Fastest" => CompressionLevel.Fastest,
        "NoCompression" => CompressionLevel.NoCompression,
        _ => CompressionLevel.Optimal
    };

    private static bool AcceptsEncoding(string headerValue, string encoding)
    {
        foreach (var part in headerValue.Split(','))
        {
            var segment = part.AsSpan().Trim();
            if (!segment.StartsWith(encoding, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = segment[encoding.Length..].TrimStart();
            if (rest.StartsWith(";q=", StringComparison.OrdinalIgnoreCase))
            {
                var quality = rest[3..];
                var terminator = quality.IndexOf(';');
                if (terminator >= 0) quality = quality[..terminator];
                if (double.TryParse(quality.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value <= 0)
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    private static void AppendVaryAcceptEncoding(IHeaderDictionary headers)
    {
        var vary = headers.Vary.ToString();
        if (!vary.Contains("Accept-Encoding", StringComparison.OrdinalIgnoreCase))
        {
            headers.Vary = string.IsNullOrEmpty(vary) ? "Accept-Encoding" : vary + ", Accept-Encoding";
        }
    }
}
