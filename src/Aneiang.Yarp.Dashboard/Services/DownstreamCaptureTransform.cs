using Aneiang.Yarp.Dashboard.Models;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Captures the actual downstream request (after all transforms, encryption, etc.) 
/// before YARP sends it. Stores data in HttpContext.Items for middleware consumption.
/// 捕获 YARP 实际发送到下游的请求（经过所有变换、加密等处理后）。
/// 将数据存入 HttpContext.Items 供中间件消费.
/// </summary>
internal sealed class DownstreamCaptureTransform : RequestTransform
{
    public override ValueTask ApplyAsync(RequestTransformContext context)
    {
        var request = context.ProxyRequest;
        context.HttpContext.Items["DownstreamMethod"] = request.Method.Method;
        context.HttpContext.Items["DownstreamUrl"] = request.RequestUri?.ToString();

        if (request.Content != null)
        {
            // Read the downstream body (after transforms)
            // YARP buffers the body when transforms are present, so this is safe
            var bodyBytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            context.HttpContext.Items["DownstreamBody"] = bodyBytes;
        }

        return default;
    }
}

/// <summary>
/// Registers DownstreamCaptureTransform for all YARP routes.
/// 为所有 YARP 路由注册 DownstreamCaptureTransform.
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
        if (!_options.Value.EnableProxyLogging)
            return;

        // Add at the END so it runs after all other transforms
        context.RequestTransforms?.Add(new DownstreamCaptureTransform());
    }

    public void ValidateRoute(TransformRouteValidationContext context) { }
    public void ValidateCluster(TransformClusterValidationContext context) { }
}
