using Aneiang.Yarp.Plugins;
using Aneiang.Yarp.Services;

namespace Aneiang.Yarp.Plugin.ProxyLog.Services;

internal sealed record RouteProxyLogSettings(
    bool CaptureRequestHeaders,
    bool CaptureResponseHeaders,
    bool RequestBodyCaptureEnabled,
    bool ResponseBodyCaptureEnabled,
    int MaxBodyLength,
    int MaxBodyBufferBytes,
    bool ErrorsOnly,
    bool SamplingEnabled,
    double SamplingRate);

internal static class RouteProxyLogSettingsResolver
{
    public static bool TryResolve(
        GatewayPluginExecutionPlan plan,
        string? routeId,
        ProxyLogRuntimeSnapshot defaults,
        out RouteProxyLogSettings settings)
    {
        settings = default!;
        if (string.IsNullOrWhiteSpace(routeId) || !plan.ProxyLogByRoute.TryGetValue(routeId, out var binding))
            return false;

        settings = new RouteProxyLogSettings(
            binding.CaptureRequestHeaders ?? true,
            binding.CaptureResponseHeaders ?? true,
            binding.RequestBodyCaptureEnabled ?? binding.EnableRequestBodyCapture ?? binding.EnableProxyRequestBodyCapture
                ?? defaults.RequestBodyCaptureEnabled,
            binding.ResponseBodyCaptureEnabled ?? binding.EnableResponseBodyCapture ?? binding.EnableProxyResponseBodyCapture
                ?? defaults.ResponseBodyCaptureEnabled,
            Math.Clamp(binding.MaxBodyLength ?? binding.LogMaxBodyLength ?? defaults.MaxBodyLength, 0, 1024 * 1024),
            Math.Clamp(binding.MaxBodyBufferBytes ?? binding.LogMaxBodyBufferBytes ?? defaults.MaxBodyBufferBytes, 0, 1024 * 1024),
            binding.ErrorsOnly ?? binding.LogErrorsOnly ?? defaults.ErrorsOnly,
            binding.SamplingEnabled ?? binding.EnableSampling ?? binding.EnableLogSampling ?? defaults.SamplingEnabled,
            Math.Clamp(binding.SamplingRate ?? binding.LogSamplingRate ?? defaults.SamplingRate, 0d, 1d));
        return true;
    }
}
