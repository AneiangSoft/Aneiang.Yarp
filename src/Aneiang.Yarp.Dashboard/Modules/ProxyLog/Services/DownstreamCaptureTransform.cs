using Aneiang.Yarp.Dashboard.Infrastructure;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Captures downstream request metadata after all configured YARP transforms.
/// Request body logging reuses the already buffered upstream body to avoid a second full copy.
/// </summary>
internal sealed class DownstreamCaptureTransform : RequestTransform
{
    public override ValueTask ApplyAsync(RequestTransformContext context)
    {
        var request = context.ProxyRequest;
        context.HttpContext.Items["DownstreamMethod"] = request.Method.Method;
        context.HttpContext.Items["DownstreamUrl"] = request.RequestUri?.ToString();
        return ValueTask.CompletedTask;
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

        // Add at the END so metadata reflects the final transformed request.
        context.RequestTransforms?.Add(new DownstreamCaptureTransform());
    }

    public void ValidateRoute(TransformRouteValidationContext context) { }
    public void ValidateCluster(TransformClusterValidationContext context) { }
}
