using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Infrastructure.HostedServices;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Services;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
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
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
            .BindConfiguration(DashboardOptions.SectionName)
            .Configure<IConfiguration>((options, config) =>
            {
                var controlPlane = config.GetSection(ControlPlaneSecurityOptions.SectionName).Get<ControlPlaneSecurityOptions>();
                if (controlPlane == null || string.IsNullOrWhiteSpace(controlPlane.AuthMode)) return;

                // Explicit Dashboard auth config still wins. Unified control-plane config fills only when Dashboard auth is None.
                if (options.AuthMode != DashboardAuthMode.None || options.AuthorizeRequest != null) return;

                if (string.Equals(controlPlane.AuthMode, "ApiKey", StringComparison.OrdinalIgnoreCase))
                {
                    options.AuthMode = DashboardAuthMode.ApiKey;
                    options.ApiKey = controlPlane.ApiKey;
                    options.ApiKeyHeaderName = string.IsNullOrWhiteSpace(controlPlane.ApiKeyHeaderName) ? "X-Api-Key" : controlPlane.ApiKeyHeaderName;
                }
                else if (string.Equals(controlPlane.AuthMode, "CustomJwt", StringComparison.OrdinalIgnoreCase))
                {
                    options.AuthMode = DashboardAuthMode.CustomJwt;
                    options.JwtUsername = controlPlane.Username ?? "admin";
                    options.JwtPassword = controlPlane.Password;
                }
                else if (string.Equals(controlPlane.AuthMode, "DefaultJwt", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(controlPlane.AuthMode, "BasicAuth", StringComparison.OrdinalIgnoreCase))
                {
                    options.AuthMode = DashboardAuthMode.DefaultJwt;
                    options.JwtPassword = controlPlane.Password;
                }
            });

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

        // Register configuration history / snapshot options
        services.AddOptions<ConfigHistoryOptions>()
            .BindConfiguration(ConfigHistoryOptions.SectionName)
            .PostConfigure(options =>
            {
                options.MaxSnapshots = Math.Max(1, options.MaxSnapshots);
                options.SnapshotQueueCapacity = Math.Max(1, options.SnapshotQueueCapacity);
            });

        #region Deployment options
        // BindConfiguration provides the raw config values. AddAneiangYarpDeployment
        // (if called) will PostConfigure to normalize Mode (Auto→Split/AllInOne).
        services.AddOptions<DeploymentOptions>()
            .BindConfiguration(DeploymentOptions.SectionName);

        #endregion

        #region Alert service (no-op default; can be replaced by user's implementation)
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Alert.IGatewayAlertService,
            Aneiang.Yarp.Dashboard.Infrastructure.Alert.NullGatewayAlertService>();

        if (configureOptions != null)
            services.Configure(configureOptions);

        // Sync WafOptions.DashboardRoutePrefix from DashboardOptions after configuration loads
        services.PostConfigure<WafOptions>(wafo =>
        {
            if (string.IsNullOrWhiteSpace(wafo.DashboardRoutePrefix))
                wafo.DashboardRoutePrefix = "apigateway";
        });

        // Register MVC controllers from this assembly with JSON camelCase naming policy.
        // Comments and trailing commas are tolerated so route/cluster editors and config import
        // accept relaxed JSON (matching docs/yarp_all.json style).
        services.AddMvcCore()
            .AddApplicationPart(typeof(DashboardPagesController).Assembly)
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip;
                options.JsonSerializerOptions.AllowTrailingCommas = true;
            });

        // Let DashboardAuth/DashboardPages controllers also search Views/Dashboard/
        services.Configure<RazorViewEngineOptions>(o =>
            o.ViewLocationExpanders.Add(new DashboardViewLocationExpander()));

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
                "image/svg+xml", "font/woff", "font/woff2", "font/ttf",
                "application/wasm",
            };
        });
        services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = System.IO.Compression.CompressionLevel.Optimal;
        });
        services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = System.IO.Compression.CompressionLevel.Optimal;
        });

        #endregion

        #region Storage backend
        services.AddAneiangStorage();

        // Register DynamicYarpConfigService as HostedService AFTER SqliteSchemaMigrator
        // so that SQLite tables exist when it loads config from repository.
        services.AddHostedService(sp => sp.GetRequiredService<Aneiang.Yarp.Services.DynamicYarpConfigService>());

        #endregion

        #region Audit log
        services.AddSingleton<IConfigChangeAuditLog, ConfigChangeAuditLog>();
        services.AddSingleton<ConfigChangeAuditLog>(sp => (ConfigChangeAuditLog)sp.GetRequiredService<IConfigChangeAuditLog>());
        services.AddSingleton<ConfigChangeEventDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<ConfigChangeEventDispatcher>());

        #endregion

        #region Rate limiting
        services.AddSingleton<RateLimitConfigProvider>();
        services.AddRateLimiter(_ => { });

        #endregion

        #region Gateway API auth
        Aneiang.Yarp.Extensions.AneiangYarpServiceCollectionExtensions.AddGatewayApiAuth(services);
        services.AddSingleton<GatewayApiAuthFilter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<MvcOptions>, GatewayApiAuthMvcOptionsSetup>());

        #endregion

        #region Proxy log store
        services.AddSingleton<IProxyLogStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DashboardOptions>>().Value;
            return new ProxyLogStore(opts.LogBufferCapacity);
        });
        services.AddSingleton<ProxyLogStore>(sp => (ProxyLogStore)sp.GetRequiredService<IProxyLogStore>());
        services.AddSingleton<LogSanitizer>();

        #endregion

        #region Downstream capture transform
        services.AddSingleton<ITransformProvider, DownstreamCaptureTransformProvider>();

        #endregion

        #region Dashboard query services
        services.AddSingleton<IDashboardInfoQueryService, DashboardInfoQueryService>();
        services.AddSingleton<IDashboardClusterQueryService, DashboardClusterQueryService>();
        services.AddSingleton<IDashboardRouteQueryService, DashboardRouteQueryService>();
        services.AddSingleton<IDashboardLogQueryService, DashboardLogQueryService>();

        #endregion

        #region Editable policy
        services.AddSingleton<IEditablePolicy, DashboardEditablePolicy>();

        #endregion

        #region WAF event store (in-memory ring buffer)
        services.AddSingleton<WafEventStore>();
        #endregion

        #region Policy services (route + cluster policies via IPolicyRepository)
        services.AddSingleton<RoutePolicyService>();
        services.AddSingleton<ClusterPolicyService>();
        services.AddSingleton<IGatewayPolicyService, GatewayPolicyService>();

        #endregion

        #region Plugin system
        services.AddSingleton<IGatewayPlugin, CircuitBreakerPlugin>();
        services.AddSingleton<IGatewayPlugin, RequestRetryPlugin>();
        services.AddSingleton<IGatewayPlugin, RateLimitPlugin>();
        services.AddSingleton<IGatewayPlugin, WafPlugin>();
        services.AddSingleton<IGatewayPluginManager, GatewayPluginManager>();
        #endregion

        #region Authorization service
        services.AddSingleton<IDashboardAuthorizationService, DashboardAuthorizationService>();

        #endregion

        #region New Unified Notification System
        // INotificationRepository is registered by AddAneiangStorage above.
        services.AddHttpClient("notification");
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddBackgroundHostedService<NotificationWarmupService>();

        #endregion

        #region WAF settings persistence
        services.AddSingleton<WafSettingsPersistenceService>();
        services.AddSingleton<IWafSettingsPersistenceService>(sp => sp.GetRequiredService<WafSettingsPersistenceService>());

        #endregion

        #region Config persistence / identity services
        services.AddSingleton<ConfigPersistenceService>();
        services.AddSingleton<IConfigPersistenceService>(sp => sp.GetRequiredService<ConfigPersistenceService>());
        services.AddSingleton<IConfigDiffService, ConfigDiffService>();
        services.AddSingleton<ConfigSnapshotScheduler>();
        services.AddSingleton<IConfigSnapshotScheduler>(sp => sp.GetRequiredService<ConfigSnapshotScheduler>());
        services.AddHostedService(sp => sp.GetRequiredService<ConfigSnapshotScheduler>());
        services.AddSingleton<IGatewayIdentityService, GatewayIdentityService>();

        #endregion

        #region Default health check service (background — non-blocking)
        services.AddBackgroundHostedService<DefaultHealthCheckService>();

        #endregion

        #region Circuit breaker warmup (background — non-blocking)
        services.AddBackgroundHostedService<CircuitBreakerWarmupService>();

        #endregion

        #region Startup warmup (background — non-blocking)
        services.AddBackgroundHostedService<StartupWarmupService>();

        #endregion

        #region Real-time traffic broadcast
        services.AddSingleton<TrafficBroadcastService>();
        services.AddHostedService<TrafficBroadcastService>();

        #endregion

        #region JWT secret provider
        services.AddSingleton<JwtSecretProvider>();

        #endregion

        #region Route prefix + auth conventions
        services.AddSingleton<IConfigureOptions<MvcOptions>, DashboardMvcOptionsSetup>();

        #endregion

        return services;
    }

    /// <summary>
    /// Register deployment-related services (EndpointRoleResolver, config validators, snapshot store, hot-reload).
    /// Configuration is resolved from DI, so this can be called as <c>services.AddAneiangYarpDeployment()</c>.
    /// </summary>
    public static IServiceCollection AddAneiangYarpDeployment(this IServiceCollection services)
    {
        services.AddOptions<DeploymentOptions>()
            .BindConfiguration(DeploymentOptions.SectionName);


        services.TryAddSingleton<DeploymentRestartState>();
        services.TryAddSingleton<EndpointRoleResolver>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var options = sp.GetRequiredService<IOptions<DeploymentOptions>>().Value;
            options.ResolvedEndpoints.Clear();
            return new EndpointRoleResolver(configuration, options);
        });

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DeploymentConfigValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, Aneiang.Yarp.Dashboard.Infrastructure.HostedServices.KestrelEndpointChangeDetector>());
        return services;
    }

}
