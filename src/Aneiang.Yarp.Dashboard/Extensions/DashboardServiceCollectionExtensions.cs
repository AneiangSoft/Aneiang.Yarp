using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Infrastructure.HostedServices;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
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
using Microsoft.IO;
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
        services.AddDashboardOptions(configureOptions);
        services.AddDashboardWebInfrastructure();
        services.AddDashboardStorageAndAudit();
        services.AddDashboardSecurity();
        services.AddDashboardProxyLog();
        services.AddDashboardQueryServices();
        services.AddDashboardWafAndPolicy();
        services.AddDashboardNotificationAndPlugins();
        services.AddDashboardRealtimeAndPerformance();
        services.AddDashboardConfigPersistence();
        services.AddDashboardWarmupServices();
        return services;
    }

    #region Option binding

    private static IServiceCollection AddDashboardOptions(
        this IServiceCollection services,
        Action<DashboardOptions>? configureOptions)
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

        services.AddOptions<CircuitBreakerOptions>()
            .BindConfiguration("Gateway:Dashboard:CircuitBreaker");
        services.AddOptions<RetryOptions>()
            .BindConfiguration("Gateway:Dashboard:Retry");
        services.AddOptions<RateLimitOptions>()
            .BindConfiguration("Gateway:Dashboard:RateLimit");
        services.AddOptions<WafOptions>()
            .BindConfiguration("Gateway:Dashboard:Waf");
        services.AddOptions<ConfigHistoryOptions>()
            .BindConfiguration(ConfigHistoryOptions.SectionName)
            .PostConfigure(options =>
            {
                options.MaxSnapshots = Math.Max(1, options.MaxSnapshots);
                options.SnapshotQueueCapacity = Math.Max(1, options.SnapshotQueueCapacity);
            });

        // Deployment options — BindConfiguration provides raw config; AddAneiangYarpDeployment
        // (if called) will PostConfigure to normalize Mode (Auto→Split/AllInOne).
        services.AddOptions<DeploymentOptions>()
            .BindConfiguration(DeploymentOptions.SectionName);

        // Alert service (no-op default; can be replaced by user's implementation)
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

        return services;
    }

    #endregion

    #region Web infrastructure (MVC, Razor, SignalR, compression)

    private static IServiceCollection AddDashboardWebInfrastructure(this IServiceCollection services)
    {
        // MVC controllers with JSON camelCase naming policy.
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

        services.AddMemoryCache();
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

        return services;
    }

    #endregion

    #region Storage, audit, rate limiting

    private static IServiceCollection AddDashboardStorageAndAudit(this IServiceCollection services)
    {
        services.AddAneiangStorage();

        // DynamicYarpConfigService loads config from SQLite on StartAsync.
        // Schema migration is now triggered lazily by SqliteConnectionFactory on first
        // connection use, so tables are guaranteed to exist regardless of registration order.
        services.AddHostedService(sp => sp.GetRequiredService<Aneiang.Yarp.Services.DynamicYarpConfigService>());

        services.AddSingleton<IConfigChangeAuditLog, ConfigChangeAuditLog>();
        services.AddSingleton<ConfigChangeAuditLog>(sp => (ConfigChangeAuditLog)sp.GetRequiredService<IConfigChangeAuditLog>());
        services.AddSingleton<ConfigChangeEventDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<ConfigChangeEventDispatcher>());

        services.AddSingleton<RateLimitConfigProvider>();
        services.AddRateLimiter(_ => { });

        return services;
    }

    #endregion

    #region Security (Gateway API auth, JWT, MVC conventions)

    private static IServiceCollection AddDashboardSecurity(this IServiceCollection services)
    {
        Aneiang.Yarp.Extensions.AneiangYarpServiceCollectionExtensions.AddGatewayApiAuth(services);
        services.AddSingleton<GatewayApiAuthFilter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<MvcOptions>, GatewayApiAuthMvcOptionsSetup>());

        services.AddSingleton<IDashboardAuthorizationService, DashboardAuthorizationService>();
        services.AddSingleton<JwtSecretProvider>();
        services.AddSingleton<IConfigureOptions<MvcOptions>, DashboardMvcOptionsSetup>();

        return services;
    }

    #endregion

    #region Proxy log store + persistence

    private static IServiceCollection AddDashboardProxyLog(this IServiceCollection services)
    {
        services.AddSingleton<IProxyLogStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DashboardOptions>>().Value;
            return new ProxyLogStore(opts.LogBufferCapacity);
        });
        services.AddSingleton<ProxyLogStore>(sp => (ProxyLogStore)sp.GetRequiredService<IProxyLogStore>());
        services.AddSingleton<LogSanitizer>();

        // SqliteProxyLogWriter: converts LogEntry → Entity and delegates to IProxyLogRepository
        services.AddSingleton<SqliteProxyLogWriter>();
        // AsyncLogPersistenceService: background service that reads from Channel and writes batches to SQLite
        services.AddSingleton<AsyncLogPersistenceService>();
        services.AddSingleton<IProxyLogPersistenceService>(sp => sp.GetRequiredService<AsyncLogPersistenceService>());
        services.AddHostedService(sp => sp.GetRequiredService<AsyncLogPersistenceService>());

        // LogSettingsService: UI-configurable log settings (SQLite overrides + IOptionsMonitor + cache)
        services.AddSingleton<LogSettingsService>();

        return services;
    }

    #endregion

    #region Query services + editable policy

    private static IServiceCollection AddDashboardQueryServices(this IServiceCollection services)
    {
        services.AddSingleton<IDashboardInfoQueryService, DashboardInfoQueryService>();
        services.AddSingleton<IDashboardClusterQueryService, DashboardClusterQueryService>();
        services.AddSingleton<IDashboardRouteQueryService, DashboardRouteQueryService>();
        services.AddSingleton<IDashboardLogQueryService, DashboardLogQueryService>();
        services.AddSingleton<IEditablePolicy, DashboardEditablePolicy>();

        return services;
    }

    #endregion

    #region WAF + policy services

    private static IServiceCollection AddDashboardWafAndPolicy(this IServiceCollection services)
    {
        services.AddSingleton<WafEventStore>();
        services.AddHostedService<WafEventPersistenceService>();

        services.AddSingleton<RoutePolicyService>();
        services.AddSingleton<ClusterPolicyService>();
        services.AddSingleton<IGatewayPolicyService, GatewayPolicyService>();

        services.AddSingleton<WafSettingsPersistenceService>();
        services.AddSingleton<IWafSettingsPersistenceService>(sp => sp.GetRequiredService<WafSettingsPersistenceService>());

        return services;
    }

    #endregion

    #region Notification + plugin system

    private static IServiceCollection AddDashboardNotificationAndPlugins(this IServiceCollection services)
    {
        // INotificationRepository is registered by AddAneiangStorage above.
        services.AddHttpClient("notification");
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddBackgroundHostedService<NotificationWarmupService>();

        services.AddSingleton<IGatewayPlugin, CircuitBreakerPlugin>();
        services.AddSingleton<IGatewayPlugin, RequestRetryPlugin>();
        services.AddSingleton<IGatewayPlugin, RateLimitPlugin>();
        services.AddSingleton<IGatewayPlugin, WafPlugin>();
        services.AddSingleton<IGatewayPluginManager, GatewayPluginManager>();

        return services;
    }

    #endregion

    #region Real-time, performance, statistics

    private static IServiceCollection AddDashboardRealtimeAndPerformance(this IServiceCollection services)
    {
        services.AddSingleton<TrafficBroadcastService>();
        services.AddHostedService<TrafficBroadcastService>();

        services.AddSingleton<RecyclableMemoryStreamManager>();
        services.AddSingleton<LockFreeStatistics>();
        services.AddSingleton<ITransformProvider, DownstreamCaptureTransformProvider>();

        return services;
    }

    #endregion

    #region Config persistence + identity + health

    private static IServiceCollection AddDashboardConfigPersistence(this IServiceCollection services)
    {
        services.AddSingleton<ConfigPersistenceService>();
        services.AddSingleton<IConfigPersistenceService>(sp => sp.GetRequiredService<ConfigPersistenceService>());
        services.AddSingleton<IConfigDiffService, ConfigDiffService>();
        services.AddSingleton<ConfigSnapshotScheduler>();
        services.AddSingleton<IConfigSnapshotScheduler>(sp => sp.GetRequiredService<ConfigSnapshotScheduler>());
        services.AddHostedService(sp => sp.GetRequiredService<ConfigSnapshotScheduler>());
        services.AddSingleton<IGatewayIdentityService, GatewayIdentityService>();

        services.AddBackgroundHostedService<DefaultHealthCheckService>();

        return services;
    }

    #endregion

    #region Warmup services

    private static IServiceCollection AddDashboardWarmupServices(this IServiceCollection services)
    {
        services.AddBackgroundHostedService<CircuitBreakerWarmupService>();
        services.AddBackgroundHostedService<StartupWarmupService>();
        return services;
    }

    #endregion

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
