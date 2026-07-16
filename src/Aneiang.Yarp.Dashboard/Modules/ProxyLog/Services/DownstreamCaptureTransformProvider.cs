using Aneiang.Yarp.Dashboard.Infrastructure;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

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
