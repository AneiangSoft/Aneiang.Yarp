using System.Collections.Concurrent;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Infrastructure.HostedServices;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Aneiang.Yarp.Infrastructure.Performance;
using Aneiang.Yarp.Plugins.CircuitBreaker.Services;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Infrastructure.Realtime;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Plugin.Cache;
using Aneiang.Yarp.Plugin.CircuitBreaker;
using Aneiang.Yarp.Plugin.Compression;
using Aneiang.Yarp.Plugin.Metrics;
using Aneiang.Yarp.Plugin.ProxyLog;
using Aneiang.Yarp.Plugin.ProxyLog.Services;
using Aneiang.Yarp.Plugin.RateLimit;
using Aneiang.Yarp.Plugin.RateLimit.Redis;
using Aneiang.Yarp.Plugin.Retry;
using Aneiang.Yarp.Plugin.ServiceDiscovery;
using Aneiang.Yarp.Plugin.Waf;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
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
using Aneiang.Yarp.Infrastructure.State;
using Aneiang.Yarp.Infrastructure.Resilience;
using Aneiang.Yarp.Plugins;

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
                if (options.Auth.AuthMode != DashboardAuthMode.None || options.Auth.AuthorizeRequest != null) return;

                if (string.Equals(controlPlane.AuthMode, "ApiKey", StringComparison.OrdinalIgnoreCase))
                {
                    options.Auth.AuthMode = DashboardAuthMode.ApiKey;
                    options.Auth.ApiKey = controlPlane.ApiKey;
                    options.Auth.ApiKeyHeaderName = string.IsNullOrWhiteSpace(controlPlane.ApiKeyHeaderName) ? "X-Api-Key" : controlPlane.ApiKeyHeaderName;
                }
                else if (string.Equals(controlPlane.AuthMode, "CustomJwt", StringComparison.OrdinalIgnoreCase))
                {
                    options.Auth.AuthMode = DashboardAuthMode.CustomJwt;
                    options.Auth.JwtUsername = controlPlane.Username ?? "admin";
                    options.Auth.JwtPassword = controlPlane.Password;
                }
                else if (string.Equals(controlPlane.AuthMode, "DefaultJwt", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(controlPlane.AuthMode, "BasicAuth", StringComparison.OrdinalIgnoreCase))
                {
                    options.Auth.AuthMode = DashboardAuthMode.DefaultJwt;
                    options.Auth.JwtPassword = controlPlane.Password;
                }
            });

        // Sub-options: Auth and ProxyLog can be injected independently
        services.AddOptions<DashboardAuthOptions>()
            .BindConfiguration("Gateway:Dashboard:Auth");
        services.AddOptions<ProxyLogOptions>()
            .BindConfiguration("Gateway:Dashboard:ProxyLog");

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
            .AddApplicationPart(typeof(Aneiang.Yarp.Plugins.CircuitBreaker.Controllers.CircuitBreakerController).Assembly)
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
        // Storage backend (e.g. AddAneiangStorage) is registered by the host application.
        // Dashboard only depends on Aneiang.Yarp.Storage.Abstractions interfaces.

        // DynamicYarpConfigService loads config from storage on StartAsync.
        // Schema migration is triggered lazily by the connection factory on first
        // connection use, so tables are guaranteed to exist regardless of registration order.
        services.AddHostedService(sp => sp.GetRequiredService<Aneiang.Yarp.Services.DynamicYarpConfigService>());

        services.AddSingleton<IConfigChangeAuditLog, ConfigChangeAuditLog>();
        services.AddSingleton<ConfigChangeAuditLog>(sp => (ConfigChangeAuditLog)sp.GetRequiredService<IConfigChangeAuditLog>());
        services.AddSingleton<ConfigChangeEventDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<ConfigChangeEventDispatcher>());

        // In-memory state stores (singleton to share state across middleware instances)
        services.AddSingleton<ICircuitStateStore, InMemoryCircuitStateStore>();
        services.AddSingleton<Aneiang.Yarp.Infrastructure.Resilience.IDestinationCandidateCoordinator, Infrastructure.Resilience.DestinationCandidateCoordinator>();
        services.AddSingleton<IRateLimiterStore, InMemoryRateLimiterStore>();

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
        // Register ProxyLogPluginOptions mapped from DashboardOptions.ProxyLog
        services.AddSingleton<IConfigureOptions<ProxyLogPluginOptions>>(sp =>
        {
            return new ConfigureNamedOptions<ProxyLogPluginOptions>(Options.DefaultName, pluginOpts =>
            {
                var dashboardOpts = sp.GetRequiredService<IOptions<DashboardOptions>>().Value;
                var logOpts = dashboardOpts.ProxyLog;
                pluginOpts.LogPersistenceEnabled = logOpts.LogPersistenceEnabled;
                pluginOpts.LogMetaRetentionDays = logOpts.LogMetaRetentionDays;
                pluginOpts.LogBodyRetentionDays = logOpts.LogBodyRetentionDays;
                pluginOpts.EnableProxyRequestBodyCapture = logOpts.EnableProxyRequestBodyCapture;
                pluginOpts.EnableProxyResponseBodyCapture = logOpts.EnableProxyResponseBodyCapture;
                pluginOpts.LogMaxBodyLength = logOpts.LogMaxBodyLength;
                pluginOpts.LogMaxBodyBufferBytes = logOpts.LogMaxBodyBufferBytes;
                pluginOpts.EnableLogSampling = logOpts.EnableLogSampling;
                pluginOpts.LogSamplingRate = logOpts.LogSamplingRate;
                pluginOpts.LogErrorsOnly = logOpts.LogErrorsOnly;
                pluginOpts.MinLogLevel = logOpts.MinLogLevel;
                pluginOpts.LogBufferCapacity = logOpts.LogBufferCapacity;
                pluginOpts.LogHeaderBlacklist = logOpts.LogHeaderBlacklist ?? [];
                pluginOpts.LogQueryBlacklist = logOpts.LogQueryBlacklist ?? [];
                pluginOpts.LogJsonFieldSanitizeList = logOpts.LogJsonFieldSanitizeList ?? [];
            });
        });

        services.TryAddSingleton<ProxyLogRuntimeSettings>();
        services.AddSingleton<IProxyLogStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DashboardOptions>>().Value;
            var runtimeSettings = sp.GetRequiredService<ProxyLogRuntimeSettings>();
            return new ProxyLogStore(
                opts.ProxyLog.LogBufferCapacity,
                persistenceEnabled: opts.ProxyLog.LogPersistenceEnabled,
                runtimeSettings: runtimeSettings);
        });
        services.AddSingleton<ProxyLogStore>(sp => (ProxyLogStore)sp.GetRequiredService<IProxyLogStore>());
        services.AddSingleton<LogSanitizer>();
        services.AddSingleton<ILogSanitizer>(sp => sp.GetRequiredService<LogSanitizer>());

        // Log sampling + filtering (extracted from YarpRequestCaptureMiddleware)
        services.AddSingleton<ILogSampler, LogSampler>();
        services.AddSingleton<ILogFilter, LogFilter>();
        services.AddSingleton<IProxyLogCapture, ProxyLogCapture>();

        // SqliteProxyLogWriter: converts LogEntry → Entity and delegates to IProxyLogRepository
        services.AddSingleton<SqliteProxyLogWriter>();
        // AsyncLogPersistenceService: background service that reads from Channel and writes batches to SQLite
        services.AddSingleton<AsyncLogPersistenceService>();
        services.AddSingleton<IProxyLogPersistenceService>(sp => sp.GetRequiredService<AsyncLogPersistenceService>());
        services.AddSingleton<IPluginRuntimeResource>(sp => sp.GetRequiredService<AsyncLogPersistenceService>());

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

        return services;
    }

    #endregion

    #region Notification + plugin system

    private static IServiceCollection AddDashboardNotificationAndPlugins(this IServiceCollection services)
    {
        // Notification module removed in Phase 6 cleanup.

        services.AddSingleton<IGatewayPlugin, CircuitBreakerPlugin>();
        services.AddSingleton<IGatewayPlugin, RequestRetryPlugin>();
        services.AddSingleton<IGatewayPlugin, RateLimitPlugin>();
        // Optional Redis-backed distributed rate limiting: active only when a route binding
        // supplies a redisConnectionString (StackExchange.Redis is loaded via reflection when present).
        services.AddSingleton<IGatewayPlugin, RedisRateLimitPlugin>();
        services.AddSingleton<IDistributedRateLimitStore, RedisLuaRateLimitStore>();
        services.AddSingleton<IGatewayPlugin, WafPlugin>();
        services.AddSingleton<IGatewayPlugin, ProxyLogPlugin>();
        services.AddSingleton<IGatewayPlugin, ResponseCachePlugin>();
        services.AddSingleton<IGatewayPlugin, TrafficMetricsPlugin>();
        services.AddSingleton<IGatewayPlugin, ClusterMetricsPlugin>();
        services.AddSingleton<IGatewayPlugin, HttpServiceDiscoveryPlugin>();
        services.AddSingleton<IGatewayPlugin, CompressionPlugin>();
        services.AddSingleton<PluginMetricStore>();
        services.AddSingleton<ExternalGatewayPluginHost>();
        services.AddSingleton<GatewayPluginManager>();
        services.AddSingleton<IGatewayPluginManager>(provider => provider.GetRequiredService<GatewayPluginManager>());
        services.AddSingleton<IPluginDashboardBuilder, PluginDashboardBuilder>();
        services.AddSingleton<PluginDashboardInitializer>();
        services.AddSingleton<IPluginRuntimeDomainManager, PluginRuntimeDomainManager>();
        services.AddSingleton<IPluginBindingMutationService, PluginBindingMutationService>();
        services.AddSingleton(provider => (PluginRuntimeDomainManager)provider.GetRequiredService<IPluginRuntimeDomainManager>());
        services.AddHostedService<PluginRuntimeDomainInitializer>();
        services.AddSingleton<IPluginActivationState>(provider => provider.GetRequiredService<GatewayPluginManager>());
        services.AddSingleton<GatewayPluginExecutionPlanProvider>();
        services.AddSingleton<IPluginManifestCatalog>(provider => provider.GetRequiredService<IGatewayPluginManager>());
        services.AddSingleton<IPluginConfigurationSchemaValidator, PluginConfigurationSchemaValidator>();
        services.AddSingleton<IPluginConfigurationMigrationService, PluginConfigurationMigrationService>();
        services.AddSingleton<IPluginConfigurationMigrator, CircuitBreakerConfigMigrator>();
        services.AddSingleton<IPluginConfigurationMigrator, RetryConfigMigrator>();
        services.AddSingleton<IPluginConfigurationMigrator, WafConfigMigrator>();
        services.AddSingleton<IPluginConfigurationMigrator, RateLimitConfigMigrator>();
        services.AddSingleton<IPluginConfigurationMigrator, ProxyLogConfigMigrator>();
        services.AddSingleton<CircuitBreakerWarmupService>();
        services.AddSingleton<IPluginRuntimeResource>(provider => provider.GetRequiredService<CircuitBreakerWarmupService>());
        services.AddSingleton<IPluginRuntimeResource, ServiceDiscoveryRefreshService>();
        services.AddHttpClient("service-discovery");
        services.AddSingleton<PluginResourceLifecycleCoordinator>();
        services.AddSingleton<IPluginResourceLifecycleCoordinator>(provider => provider.GetRequiredService<PluginResourceLifecycleCoordinator>());
        services.AddHostedService(provider => provider.GetRequiredService<PluginResourceLifecycleCoordinator>());
        services.AddSingleton<PluginResourceMonitor>();
        services.AddSingleton<IPluginResourceMonitor>(provider => provider.GetRequiredService<PluginResourceMonitor>());

        return services;
    }

    #endregion

    #region Real-time, performance, statistics

    private static IServiceCollection AddDashboardRealtimeAndPerformance(this IServiceCollection services)
    {
        services.AddSingleton<TrafficBroadcastService>();
        services.AddHostedService<TrafficBroadcastService>();

        services.AddSingleton<OverviewBroadcastService>();
        services.AddHostedService<OverviewBroadcastService>();

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

        return services;
    }

    #endregion

    #region Warmup services

    private static IServiceCollection AddDashboardWarmupServices(this IServiceCollection services)
    {
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
