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

/// <summary>
/// Registers DownstreamCaptureTransform for all YARP routes.
/// </summary>
internal sealed class DownstreamCaptureTransformProvider : ITransformProvider
{
    private readonly IOptions<DashboardOptions> _options;

    public DownstreamCaptureTransformProvider(IOptions<DashboardOptions> options)
    {
        _options = options;
    }

    public void Apply(TransformBuilderContext context)
    {
        var options = _options.Value;
        if (!options.EnableProxyLogging || !options.EnableProxyRequestBodyCapture)
            return;

        // Add at the END so it runs after all other transforms
        context.RequestTransforms?.Add(new DownstreamCaptureTransform(options.LogMaxBodyBufferBytes));
    }

    public void ValidateRoute(TransformRouteValidationContext context) { }
    public void ValidateCluster(TransformClusterValidationContext context) { }
}
