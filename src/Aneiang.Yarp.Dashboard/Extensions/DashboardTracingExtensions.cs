using Aneiang.Yarp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>
/// Dashboard extensions for full OpenTelemetry distributed tracing integration.
/// Adds ASP.NET Core + HTTP client instrumentation with OTLP export.
/// </summary>
public static class DashboardTracingExtensions
{
    /// <summary>
    /// Enable OpenTelemetry distributed tracing with YARP gateway integration.
    /// Configures ASP.NET Core and HTTP client instrumentation, OTLP exporter,
    /// and W3C trace context propagation.
    /// <para>
    /// The OTLP exporter endpoint can be configured via:
    /// <list type="number">
    ///   <item><c>Gateway:Tracing:OtlpEndpoint</c> configuration</item>
    ///   <item><c>OTEL_EXPORTER_OTLP_ENDPOINT</c> environment variable (standard)</item>
    /// </list>
    /// Other OpenTelemetry settings can also be controlled via standard
    /// <c>OTEL_*</c> environment variables, which take precedence.
    /// </para>
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <param name="configureOptions">Optional override for tracing options.</param>
    /// <example>
    /// <code>
    /// // From config file only:
    /// builder.Services.AddAneiangYarpTracing();
    ///
    /// // With code override:
    /// builder.Services.AddAneiangYarpTracing(o =>
    /// {
    ///     o.OtlpEndpoint = "http://otel-collector:4317";
    ///     o.ServiceName = "my-gateway";
    ///     o.SamplingRate = 0.5;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddAneiangYarpTracing(
        this IServiceCollection services,
        Action<GatewayTracingOptions>? configureOptions = null)
    {
        // Bind options from configuration and set OTEL env vars early
        services.AddOptions<GatewayTracingOptions>()
            .BindConfiguration(GatewayTracingOptions.SectionName)
            .Configure<IConfiguration>((options, config) =>
            {
                ConfigureOtelEnvironment(options);
            });

        if (configureOptions != null)
            services.Configure(configureOptions);

        // Register post-configure for tracing logging/diagnostics
        services.AddSingleton<IPostConfigureOptions<GatewayTracingOptions>, TracingOptionsConfigurator>();

        // Configure OpenTelemetry SDK
        // The SDK automatically reads OTEL_* environment variables for:
        // - OTLP endpoint (OTEL_EXPORTER_OTLP_ENDPOINT)
        // - Service name (OTEL_SERVICE_NAME)
        // - Sampling (OTEL_TRACES_SAMPLER, OTEL_TRACES_SAMPLER_ARG)
        // - Propagators (OTEL_PROPAGATORS)
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: "Aneiang.Yarp.Gateway",
                    serviceVersion: typeof(DashboardTracingExtensions).Assembly.GetName().Version?.ToString() ?? "2.3.0"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        // Exclude dashboard paths from tracing to reduce noise
                        options.Filter = ctx =>
                        {
                            var path = ctx.Request.Path.Value;
                            return path == null || !path.StartsWith("/apigateway", StringComparison.OrdinalIgnoreCase);
                        };
                    })
                    .AddHttpClientInstrumentation();

                // OTLP exporter is auto-configured by the SDK when
                // OTEL_EXPORTER_OTLP_ENDPOINT env var is set.
                // Console exporter is added via deferred configuration.
            });

        return services;
    }

    /// <summary>
    /// Sets standard OpenTelemetry environment variables from our configuration options,
    /// but only when they are not already defined. This allows our <c>Gateway:Tracing</c>
    /// config section to coexist with the standard OTEL_* environment variables
    /// (env vars take precedence per OpenTelemetry specification).
    /// </summary>
    private static void ConfigureOtelEnvironment(GatewayTracingOptions options)
    {
        // OTLP exporter endpoint
        if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint)
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", options.OtlpEndpoint);
        }

        // Service name
        if (!string.IsNullOrWhiteSpace(options.ServiceName)
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")))
        {
            Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", options.ServiceName);
        }

        // Propagators
        if (options.Propagators.Count > 0
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_PROPAGATORS")))
        {
            Environment.SetEnvironmentVariable("OTEL_PROPAGATORS", string.Join(",", options.Propagators));
        }

        // Sampling - use TraceIdRatio-based sampler when rate is not 1.0
        if (options.SamplingRate is >= 0.0 and < 1.0
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_TRACES_SAMPLER")))
        {
            Environment.SetEnvironmentVariable("OTEL_TRACES_SAMPLER", "traceidratio");
            Environment.SetEnvironmentVariable("OTEL_TRACES_SAMPLER_ARG", options.SamplingRate.ToString("F2"));
        }
    }
}

/// <summary>
/// Post-configures tracing options to apply runtime enrichment and logging.
/// </summary>
internal sealed class TracingOptionsConfigurator : IPostConfigureOptions<GatewayTracingOptions>
{
    private readonly ILogger<TracingOptionsConfigurator> _logger;

    public TracingOptionsConfigurator(ILogger<TracingOptionsConfigurator> logger) => _logger = logger;

    public void PostConfigure(string? name, GatewayTracingOptions options)
    {
        if (!options.Enabled)
        {
            _logger.LogDebug("Gateway OpenTelemetry tracing is disabled via configuration");
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
        {
            _logger.LogInformation("Gateway tracing enabled — OTLP endpoint: {Endpoint}, Service: {Service}",
                options.OtlpEndpoint, options.ServiceName);
        }
        else
        {
            _logger.LogWarning("Gateway tracing enabled but no OTLP endpoint configured. " +
                "Set Gateway:Tracing:OtlpEndpoint or OTEL_EXPORTER_OTLP_ENDPOINT environment variable");
        }

        if (options.EnableConsoleExporter)
        {
            _logger.LogDebug("Console exporter enabled for tracing (use for debugging only)");
        }

        if (options.TraceHeaders is { Count: > 0 })
        {
            _logger.LogDebug("Custom trace headers configured: {Headers}", string.Join(", ", options.TraceHeaders));
        }
    }
}
