using Aneiang.Yarp.Dashboard.Infrastructure;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Captures the actual downstream request (after all transforms, encryption, etc.) 
/// before YARP sends it. Stores data in HttpContext.Items for middleware consumption.
/// </summary>
internal sealed class DownstreamCaptureTransform : RequestTransform
{
    private readonly int _maxBodyBufferBytes;

    public DownstreamCaptureTransform(int maxBodyBufferBytes)
    {
        _maxBodyBufferBytes = Math.Max(0, maxBodyBufferBytes);
    }

    public override async ValueTask ApplyAsync(RequestTransformContext context)
    {
        var request = context.ProxyRequest;
        context.HttpContext.Items["DownstreamMethod"] = request.Method.Method;
        context.HttpContext.Items["DownstreamUrl"] = request.RequestUri?.ToString();

        if (request.Content == null || _maxBodyBufferBytes <= 0)
        {
            return;
        }

        var contentLength = request.Content.Headers.ContentLength;
        var mediaType = request.Content.Headers.ContentType?.MediaType;
        if (contentLength is null or 0 || contentLength > _maxBodyBufferBytes || !IsTextLikeContent(mediaType))
        {
            return;
        }

        var bodyBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        context.HttpContext.Items["DownstreamBody"] = bodyBytes;
    }

    private static bool IsTextLikeContent(string? mediaType)
    {
        return mediaType != null &&
               (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("form", StringComparison.OrdinalIgnoreCase));
    }
}

