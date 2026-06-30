using Aneiang.Yarp.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Extensions;

/// <summary>
/// Extension methods for registering gateway OpenTelemetry tracing configuration.
/// Use this in the core gateway project. The Dashboard package provides
/// full OpenTelemetry integration via <c>AddAneiangYarpTracing</c>.
/// </summary>
public static class GatewayTracingServiceCollectionExtensions
{
    /// <summary>
    /// Register gateway tracing options bound from <c>Gateway:Tracing</c> configuration section.
    /// <para>
    /// This method only registers the options model — it does NOT add OpenTelemetry SDK services.
    /// Use <c>AddAneiangYarpTracing</c> from <c>Aneiang.Yarp.Dashboard</c> for full integration,
    /// or reference the OpenTelemetry NuGet packages directly in your project.
    /// </para>
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <param name="configureOptions">Optional override for tracing options.</param>
    /// <example>
    /// <code>
    /// // Minimal — bind from config only:
    /// builder.Services.AddGatewayTracingOptions();
    ///
    /// // With code override (highest precedence):
    /// builder.Services.AddGatewayTracingOptions(o =>
    /// {
    ///     o.ServiceName = "my-gateway";
    ///     o.OtlpEndpoint = "http://otel-collector:4317";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddGatewayTracingOptions(
        this IServiceCollection services,
        Action<GatewayTracingOptions>? configureOptions = null)
    {
        services.AddOptions<GatewayTracingOptions>()
            .BindConfiguration(GatewayTracingOptions.SectionName);

        if (configureOptions != null)
            services.Configure(configureOptions);

        return services;
    }
}
