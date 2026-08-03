using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
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
    private readonly GatewayPluginExecutionPlanProvider _executionPlans;
    private readonly ProxyLogRuntimeSettings _runtimeSettings;

    public DownstreamCaptureTransformProvider(
        GatewayPluginExecutionPlanProvider executionPlans,
        ProxyLogRuntimeSettings runtimeSettings)
    {
        _executionPlans = executionPlans;
        _runtimeSettings = runtimeSettings;
    }

    public void Apply(TransformBuilderContext context)
    {
        if (!RouteProxyLogSettingsResolver.TryResolve(
                _executionPlans.Current,
                context.Route.RouteId,
                _runtimeSettings.Current,
                out var settings) ||
            !settings.RequestBodyCaptureEnabled)
            return;

        // Add at the END so metadata reflects the final transformed request.
        context.RequestTransforms?.Add(new DownstreamCaptureTransform());
    }

    public void ValidateRoute(TransformRouteValidationContext context) { }
    public void ValidateCluster(TransformClusterValidationContext context) { }
}
