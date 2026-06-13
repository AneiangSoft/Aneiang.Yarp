using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Infrastructure.Realtime;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.Notification.Services;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.Policy.Services;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Services;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>Aneiang.Yarp.Dashboard service registration extensions.</summary>
public static class DashboardServiceCollectionExtensions
{
    /// <summary>
    /// Register Dashboard with configurable auth and route prefix.
    /// Options are bound from <c>Gateway:Dashboard</c> configuration section.
    /// </summary>
    public static IServiceCollection AddAneiangYarpDashboard(
        this IServiceCollection services,
        Action<DashboardOptions>? configureOptions = null)
    {
        services.AddOptions<DashboardOptions>()
            .BindConfiguration(DashboardOptions.SectionName);

        // Register CircuitBreaker options (nested in DashboardOptions)
        services.AddOptions<CircuitBreakerOptions>()
            .BindConfiguration("Gateway:Dashboard:CircuitBreaker");

        // Register Retry options (nested in DashboardOptions)
        services.AddOptions<RetryOptions>()
            .BindConfiguration("Gateway:Dashboard:Retry");

        // Register RateLimit options (nested in DashboardOptions)
        services.AddOptions<RateLimitOptions>()
            .BindConfiguration("Gateway:Dashboard:RateLimit");

        // Register WAF options (nested in DashboardOptions)
        services.AddOptions<WafOptions>()
            .BindConfiguration("Gateway:Dashboard:Waf");

        if (configureOptions != null)
            services.Configure(configureOptions);

        // Sync WafOptions.DashboardRoutePrefix from DashboardOptions after configuration loads
        services.PostConfigure<WafOptions>(wafo =>
        {
            if (string.IsNullOrWhiteSpace(wafo.DashboardRoutePrefix))
                wafo.DashboardRoutePrefix = "apigateway";
        });

        // Register MVC controllers from this assembly with JSON camelCase naming policy
        services.AddMvcCore()
            .AddApplicationPart(typeof(DashboardController).Assembly)
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            });

        // Unified caching: single IMemoryCache instance shared by all query services.
        services.AddMemoryCache();

        // SignalR for real-time topology traffic visualization.
        services.AddSignalR();

        // Response Compression: Brotli (preferred) + Gzip fallbacks.
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = new[]
            {
                "text/plain", "text/css", "text/javascript", "text/xml",
                "application/javascript", "application/json", "application/xml",
                "application/xml-dtd", "application/atom+xml", "application/octet-stream",
                "image/svg+xml",
            };
        });
        services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = System.IO.Compression.CompressionLevel.Fastest;
        });
        services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = System.IO.Compression.CompressionLevel.Fastest;
        });

        // ── Storage backend ──────────────────────────────────────────────────────
        services.AddAneiangStorage(services.BuildServiceProvider().GetRequiredService<IConfiguration>());

        // ── Audit log ─────────────────────────────────────────────────────────────
        services.AddSingleton<IConfigChangeAuditLog, ConfigChangeAuditLog>();
        services.AddSingleton<ConfigChangeAuditLog>(sp => (ConfigChangeAuditLog)sp.GetRequiredService<IConfigChangeAuditLog>());
        services.AddHostedService<ConfigChangeEventDispatcher>();

        // ── Rate limiting ─────────────────────────────────────────────────────────
        services.AddSingleton<RateLimitConfigProvider>();
        services.AddRateLimiter(_ => { });

        // ── Gateway API auth ──────────────────────────────────────────────────────
        services.AddSingleton<GatewayApiAuthFilter>();
        services.AddSingleton<IConfigureOptions<MvcOptions>>(_ =>
            new ConfigureNamedOptions<MvcOptions>(null, mvo =>
                mvo.Conventions.Add(new GatewayApiAuthConvention())));

        // ── Proxy log store ───────────────────────────────────────────────────────
        services.AddSingleton<IProxyLogStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DashboardOptions>>().Value;
            return new ProxyLogStore(opts.LogBufferCapacity);
        });
        services.AddSingleton<ProxyLogStore>(sp => (ProxyLogStore)sp.GetRequiredService<IProxyLogStore>());
        services.AddSingleton<LogSanitizer>();
        services.AddSingleton<YarpEventSourceListener>();
        services.AddHostedService<YarpEventSourceListenerStartupService>();

        // ── Downstream capture transform ──────────────────────────────────────────
        services.AddSingleton<ITransformProvider, DownstreamCaptureTransformProvider>();

        // ── Dashboard query services ──────────────────────────────────────────────
        services.AddSingleton<IDashboardInfoQueryService, DashboardInfoQueryService>();
        services.AddSingleton<IDashboardClusterQueryService, DashboardClusterQueryService>();
        services.AddSingleton<IDashboardRouteQueryService, DashboardRouteQueryService>();
        services.AddSingleton<IDashboardLogQueryService, DashboardLogQueryService>();

        // ── Editable policy ───────────────────────────────────────────────────────
        services.AddSingleton<IEditablePolicy, DashboardEditablePolicy>();

        // ── WAF event store (in-memory ring buffer) ──────────────────────────────
        services.AddSingleton<WafEventStore>();

        // ── Policy service (route + cluster policies via IPolicyRepository) ──────
        services.AddSingleton<IGatewayPolicyService, GatewayPolicyService>();

        // ── Config snapshot service ───────────────────────────────────────────────
        services.AddSingleton<IConfigSnapshotService, ConfigSnapshotService>();

        // ── Plugin system ─────────────────────────────────────────────────────────
        services.AddSingleton<IGatewayPlugin, CircuitBreakerPlugin>();
        services.AddSingleton<IGatewayPlugin, RequestRetryPlugin>();
        services.AddSingleton<IGatewayPlugin, RateLimitPlugin>();
        services.AddSingleton<IGatewayPlugin, WafPlugin>();
        services.AddSingleton<IGatewayPluginManager, GatewayPluginManager>();

        // ── Authorization service ─────────────────────────────────────────────────
        services.AddSingleton<IDashboardAuthorizationService, DashboardAuthorizationService>();

        // ── New Unified Notification System ─────────────────────────────────────
        // INotificationRepository is registered by AddAneiangStorage above.
        services.AddHttpClient("notification");
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddHostedService<NotificationWarmupService>();

        // ── WAF settings persistence ──────────────────────────────────────────
        services.AddSingleton<WafSettingsPersistenceService>();
        services.AddSingleton<IWafSettingsPersistenceService>(sp => sp.GetRequiredService<WafSettingsPersistenceService>());

        // ── Config persistence service ────────────────────────────────────────────
        services.AddSingleton<ConfigPersistenceService>();
        services.AddSingleton<IConfigPersistenceService>(sp => sp.GetRequiredService<ConfigPersistenceService>());

        // ── Default health check service ──────────────────────────────────────────
        services.AddHostedService<DefaultHealthCheckService>();

        // ── Startup warmup: initializes repositories, MemoryCache, and query services ──
        services.AddHostedService<StartupWarmupService>();

        // ── Real-time traffic broadcast ───────────────────────────────────────────
        services.AddHostedService<TrafficBroadcastService>();

        // ── JWT secret provider ───────────────────────────────────────────────────
        services.AddSingleton<JwtSecretProvider>();

        // ── Route prefix + auth conventions ────────────────────────────────────────
        services.AddSingleton<IConfigureOptions<MvcOptions>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DashboardOptions>>().Value;
            var prefix = opts.RoutePrefix.Trim('/');

            DashboardController.RoutePrefix = prefix;

            var secretProvider = sp.GetRequiredService<JwtSecretProvider>();
            opts.JwtSecret = secretProvider.GetSecret(opts.JwtSecret);

            return new ConfigureNamedOptions<MvcOptions>(null, mvcOptions =>
            {
                mvcOptions.Conventions.Add(new DashboardRouteConvention(prefix));

                var authFilter = CreateAuthFilter(opts, prefix);
                if (authFilter != null)
                    mvcOptions.Filters.Add(authFilter);
            });
        });

        return services;
    }

    private static DashboardAuthFilter? CreateAuthFilter(DashboardOptions opts, string routePrefix)
    {
        if (opts.AuthMode == DashboardAuthMode.None && opts.AuthorizeRequest == null)
            return null;

        return new DashboardAuthFilter(
            new DashboardAuthorizationService(Options.Create(opts),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<DashboardAuthorizationService>.Instance),
            routePrefix);
    }
}
