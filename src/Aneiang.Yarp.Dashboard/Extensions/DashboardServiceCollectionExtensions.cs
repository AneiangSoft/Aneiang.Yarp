using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Infrastructure.Filters;
using Aneiang.Yarp.Dashboard.Infrastructure.HostedServices;
using FluentValidation;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
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
using Aneiang.Yarp.Dashboard.Modules.AI;
using Aneiang.Yarp.Dashboard.Modules.AI.Providers;
using Aneiang.Yarp.Dashboard.Modules.AI.Services;
using Aneiang.Yarp.Dashboard.Modules.AI.Tools;
using Aneiang.Yarp.Dashboard.Modules.Waf.Services;
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
        services.AddDashboardAI();
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

        // Sub-options: Auth and ProxyLog can be injected independently
        services.AddOptions<DashboardAuthOptions>()
            .BindConfiguration("Gateway:Dashboard:Auth");
        services.AddOptions<ProxyLogOptions>()
            .BindConfiguration("Gateway:Dashboard:ProxyLog");

        // Sync facade: copy flat DashboardOptions values to sub-options if sub-options weren't set via config
        services.AddSingleton<IConfigureOptions<DashboardAuthOptions>>(sp =>
            new AuthOptionsSync(sp.GetRequiredService<IOptions<DashboardOptions>>()));
        services.AddSingleton<IConfigureOptions<ProxyLogOptions>>(sp =>
            new ProxyLogOptionsSync(sp.GetRequiredService<IOptions<DashboardOptions>>()));
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

        services.AddSingleton<RateLimitConfigProvider>();
        services.AddRateLimiter(_ => { });

        // In-memory state stores (singleton to share state across middleware instances)
        services.AddSingleton<ICircuitStateStore, InMemoryCircuitStateStore>();
        services.AddSingleton<IRateLimiterStore, InMemoryRateLimiterStore>();
        services.AddSingleton<CooldownManager>();

        // Persistent state store (default: file system; replaceable for containerized deployments)
        services.AddSingleton<IStateStore>(sp =>
        {
            var env = sp.GetRequiredService<IHostEnvironment>();
            var logger = sp.GetRequiredService<ILogger<FileStateStore>>();
            var dir = Path.Combine(env.ContentRootPath, "state");
            return new FileStateStore(dir, logger);
        });

        return services;
    }

    #endregion

    #region Security (Gateway API auth, JWT, MVC conventions)

    private static IServiceCollection AddDashboardSecurity(this IServiceCollection services)
    {
        Aneiang.Yarp.Extensions.AneiangYarpServiceCollectionExtensions.AddGatewayApiAuth(services);
        services.AddSingleton<GatewayApiAuthFilter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<MvcOptions>, GatewayApiAuthMvcOptionsSetup>());

        // FluentValidation: register validators from the assembly
        services.AddValidatorsFromAssemblyContaining<Infrastructure.Validation.LoginRequestValidator>();
        services.AddSingleton<Infrastructure.Filters.FluentValidationFilter>();

        services.AddSingleton<IDashboardAuthorizationService, DashboardAuthorizationService>();
        services.AddSingleton<JwtSecretProvider>();
        services.AddSingleton<GlobalExceptionFilter>();
        services.AddSingleton<DashboardRouteAccessor>();
        services.AddSingleton<IDashboardRouteAccessor>(sp => sp.GetRequiredService<DashboardRouteAccessor>());
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

        // Log sampling + filtering (extracted from YarpRequestCaptureMiddleware)
        services.AddSingleton<ILogSampler, LogSampler>();
        services.AddSingleton<ILogFilter, LogFilter>();
        services.AddSingleton<IProxyLogCapture, ProxyLogCapture>();

        // SqliteProxyLogWriter: converts LogEntry → Entity and delegates to IProxyLogRepository
        services.AddSingleton<SqliteProxyLogWriter>();
        // AsyncLogPersistenceService: background service that reads from Channel and writes batches to SQLite
        services.AddSingleton<AsyncLogPersistenceService>();
        services.AddSingleton<IProxyLogPersistenceService>(sp => sp.GetRequiredService<AsyncLogPersistenceService>());
        services.AddHostedService(sp => sp.GetRequiredService<AsyncLogPersistenceService>());

        // LogSettingsService: UI-configurable log settings (SQLite overrides + IOptionsMonitor + cache)
        services.AddSingleton<LogSettingsService>();
        services.AddSingleton<ILogSettingsService>(sp => sp.GetRequiredService<LogSettingsService>());

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

        // Application services (business logic extracted from controllers)
        services.AddSingleton<Modules.Dashboard.Application.IStatsAppService, Modules.Dashboard.Application.StatsAppService>();
        services.AddSingleton<Modules.Operations.Application.IAlertAppService, Modules.Operations.Application.AlertAppService>();
        services.AddSingleton<Modules.Operations.Application.IPerformanceAppService, Modules.Operations.Application.PerformanceAppService>();
        services.AddSingleton<Modules.Operations.Application.IHealthAppService, Modules.Operations.Application.HealthAppService>();
        services.AddSingleton<Modules.GatewayConfig.Application.IConfigHistoryAppService, Modules.GatewayConfig.Application.ConfigHistoryAppService>();
        services.AddSingleton<Modules.GatewayConfig.Application.IConfigKnowledgeService, Modules.GatewayConfig.Application.ConfigKnowledgeService>();
        services.AddSingleton<Modules.GatewayConfig.Application.IConfigTemplateService, Modules.GatewayConfig.Application.ConfigTemplateService>();
        services.AddSingleton<Modules.Features.IFeatureCatalogService, Modules.Features.FeatureCatalogService>();
        // Config Health Score engine + 12 rules
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Health.ConfigHealthService>();
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Health.IConfigHealthRule, Aneiang.Yarp.Dashboard.Infrastructure.Health.ConfigHealthRules.WafEnabledRule>();
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Health.IConfigHealthRule, Aneiang.Yarp.Dashboard.Infrastructure.Health.ConfigHealthRules.IpBlacklistRule>();
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Health.IConfigHealthRule, Aneiang.Yarp.Dashboard.Infrastructure.Health.ConfigHealthRules.RequestBodySizeRule>();
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Health.IConfigHealthRule, Aneiang.Yarp.Dashboard.Infrastructure.Health.ConfigHealthRules.HealthCheckRule>();
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Health.IConfigHealthRule, Aneiang.Yarp.Dashboard.Infrastructure.Health.ConfigHealthRules.CircuitBreakerRule>();
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Health.IConfigHealthRule, Aneiang.Yarp.Dashboard.Infrastructure.Health.ConfigHealthRules.SingleBackendRule>();
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Health.IConfigHealthRule, Aneiang.Yarp.Dashboard.Infrastructure.Health.ConfigHealthRules.RetryRule>();
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Health.IConfigHealthRule, Aneiang.Yarp.Dashboard.Infrastructure.Health.ConfigHealthRules.LoadBalancingRule>();
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Health.IConfigHealthRule, Aneiang.Yarp.Dashboard.Infrastructure.Health.ConfigHealthRules.Http2Rule>();
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Health.IConfigHealthRule, Aneiang.Yarp.Dashboard.Infrastructure.Health.ConfigHealthRules.RouteOrderRule>();
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Health.IConfigHealthRule, Aneiang.Yarp.Dashboard.Infrastructure.Health.ConfigHealthRules.ConfigSnapshotRule>();
        services.AddSingleton<Aneiang.Yarp.Dashboard.Infrastructure.Health.IConfigHealthRule, Aneiang.Yarp.Dashboard.Infrastructure.Health.ConfigHealthRules.TransformPathPatternRule>();
        services.AddSingleton<Modules.AI.Application.IAIAppService, Modules.AI.Application.AIAppService>();
        services.AddSingleton<Modules.Policy.Application.IPolicyAppService, Modules.Policy.Application.PolicyAppService>();
        services.AddSingleton<Modules.Notification.Application.INotificationAppService, Modules.Notification.Application.NotificationAppService>();

        return services;
    }

    #endregion

    #region WAF + policy services

    private static IServiceCollection AddDashboardWafAndPolicy(this IServiceCollection services)
    {
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
        // INotificationRepository is registered by the host application's storage backend (e.g. AddAneiangStorage).
        services.AddHttpClient("notification");
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddBackgroundHostedService<NotificationWarmupService>();

        services.AddSingleton<IGatewayPlugin, CircuitBreakerPlugin>();
        services.AddSingleton<IGatewayPlugin, RequestRetryPlugin>();
        services.AddSingleton<IGatewayPlugin, RateLimitPlugin>();
        services.AddSingleton<IGatewayPlugin, WafPlugin>();
        services.AddSingleton<IGatewayPlugin, AIPlugin>();
        services.AddSingleton<IGatewayPluginManager, GatewayPluginManager>();

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

    #region AI Module

    private static IServiceCollection AddDashboardAI(this IServiceCollection services)
    {
        services.AddOptions<AIOptions>()
            .BindConfiguration(AIOptions.SectionName);

        services.AddSingleton<IAIProvider, OpenAICompatibleProvider>();
        services.AddSingleton<AISettingsService>();
        services.AddSingleton<IAISettingsService>(sp => sp.GetRequiredService<AISettingsService>());
        services.AddSingleton<GatewayContextProvider>();
        services.AddSingleton<GatewayToolRegistry>();
        services.AddSingleton<GatewayToolExecutor>();
        services.AddSingleton<ChatService>();
        services.AddSingleton<IChatService>(sp => sp.GetRequiredService<ChatService>());
        services.AddSingleton<NotificationEnhancer>();

        // Background analysis service (runs only when EnableBackgroundAnalysis=true and provider is available)
        services.AddHostedService<BackgroundAIAnalysisService>();

        return services;
    }

    #endregion

}
