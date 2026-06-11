using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Infrastructure.Realtime;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.Policy.Services;
using Aneiang.Yarp.Dashboard.Modules.Alert.Services;
using Aneiang.Yarp.Dashboard.Modules.Alert.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Services;
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
        // Replaces the fragmented 3-layer cache (DashboardCacheService + ConcurrentDictionary
        // per service + IMemoryCache) with a single coherent layer.
        services.AddMemoryCache();

        // SignalR for real-time topology traffic visualization.
        // Clients on the /topology page receive live traffic updates every 2 seconds.
        services.AddSignalR();

        // Response Compression: Brotli (preferred) + Gzip fallbacks.
        // Brotli achieves ~15-25% better compression than gzip for typical web content.
        // Must be added before any middleware that writes responses.
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

        // ── Storage backend (IDataStore) ─────────────────────────────────
        services.AddAneiangStorage(services.BuildServiceProvider().GetRequiredService<IConfiguration>());

        // ── Dashboard-specific services (moved from Aneiang.Yarp) ──────────────

        // Override core service implementations with Dashboard implementations
        services.AddSingleton<IConfigChangeAuditLog, ConfigChangeAuditLog>();
        services.AddSingleton<IDynamicConfigPersistenceService, DynamicConfigPersistenceService>();

        // Preload dynamic config to avoid sync-over-async deadlocks
        services.AddHostedService<DynamicConfigPreloadService>();

        // Webhook settings persistence (also needs preload)
        services.AddSingleton<WebhookSettingsPersistenceService>();
        services.AddSingleton<IWebhookSettingsPersistenceService>(sp => sp.GetRequiredService<WebhookSettingsPersistenceService>());
        services.AddHostedService(sp => new WebhookSettingsPreloadService(
            sp.GetRequiredService<WebhookSettingsPersistenceService>()));

        // Rate limiting
        services.AddSingleton<RateLimitConfigProvider>();
        services.AddRateLimiter(_ => { });

        // Gateway API auth
        services.AddSingleton<GatewayApiAuthFilter>();
        services.AddSingleton<IConfigureOptions<MvcOptions>>(_ =>
            new ConfigureNamedOptions<MvcOptions>(null, mvo =>
                mvo.Conventions.Add(new GatewayApiAuthConvention())));

        // ── Dashboard-specific services (original) ────────────────────────────

        // YARP log capture
        services.AddSingleton<IProxyLogStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DashboardOptions>>().Value;
            return new ProxyLogStore(opts.LogBufferCapacity);
        });
        services.AddSingleton<ProxyLogStore>(sp => (ProxyLogStore)sp.GetRequiredService<IProxyLogStore>());
        services.AddSingleton<LogSanitizer>();
        services.AddSingleton<YarpEventSourceListener>();
        services.AddHostedService<YarpEventSourceListenerStartupService>();

        // Downstream capture transform
        services.AddSingleton<ITransformProvider, DownstreamCaptureTransformProvider>();

        // Dashboard query services
        services.AddSingleton<IDashboardInfoQueryService, DashboardInfoQueryService>();
        services.AddSingleton<IDashboardClusterQueryService, DashboardClusterQueryService>();
        services.AddSingleton<IDashboardRouteQueryService, DashboardRouteQueryService>();
        services.AddSingleton<IDashboardLogQueryService, DashboardLogQueryService>();

        // Editable policy
        services.AddSingleton<IEditablePolicy, DashboardEditablePolicy>();

        // Alert service
        services.AddSingleton<IGatewayAlertService, GatewayAlertService>();
        services.AddSingleton<GatewayAlertService>(sp => (GatewayAlertService)sp.GetRequiredService<IGatewayAlertService>());
        services.AddSingleton<AlertHistoryStore>(sp =>
            new AlertHistoryStore(sp.GetRequiredService<IOptions<DashboardOptions>>().Value.AlertMaxRecords));
        services.AddSingleton<WafEventStore>(sp =>
            new WafEventStore(sp.GetRequiredService<IOptions<DashboardOptions>>().Value.WafMaxEvents));

        // Policy persistence
        services.AddSingleton<GatewayPolicyPersistenceService>();
        services.AddSingleton<IGatewayPolicyPersistenceService>(sp => sp.GetRequiredService<GatewayPolicyPersistenceService>());

        // Config snapshot service
        services.AddSingleton<IConfigSnapshotService, ConfigSnapshotService>();

        // Plugin system
        services.AddSingleton<IGatewayPlugin, CircuitBreakerPlugin>();
        services.AddSingleton<IGatewayPlugin, RequestRetryPlugin>();
        services.AddSingleton<IGatewayPlugin, WafPlugin>();
        services.AddSingleton<IGatewayPluginManager, GatewayPluginManager>();

        // Authorization service
        services.AddSingleton<IDashboardAuthorizationService, DashboardAuthorizationService>();

        // Webhook notification service
        services.AddHttpClient("webhook");
        services.AddSingleton<IWebhookProvider, DingTalkWebhookProvider>();
        services.AddSingleton<IWebhookProvider, GenericWebhookProvider>();
        services.AddSingleton<WebhookNotificationService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DashboardOptions>>();
            var webhook = new WebhookNotificationService(
                options,
                sp.GetRequiredService<ILogger<WebhookNotificationService>>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetServices<IWebhookProvider>());
            webhook.Subscribe(sp.GetRequiredService<IConfigChangeAuditLog>());

            var persistence = sp.GetRequiredService<WebhookSettingsPersistenceService>();
            var data = persistence.Load() ?? new WebhookSettingsData();
            var opts = options.Value;
            var allUrls = new List<string>();
            allUrls.AddRange(data.DingTalkEndpoints.Select(e => e.Url));
            allUrls.AddRange(data.GenericEndpoints.Select(e => e.Url));
            opts.WebhookUrls = allUrls;
            opts.WebhookSecrets = new Dictionary<string, string?>();
            foreach (var ep in data.DingTalkEndpoints) opts.WebhookSecrets[ep.Url] = ep.Secret;
            foreach (var ep in data.GenericEndpoints) opts.WebhookSecrets[ep.Url] = ep.Secret;
            opts.WebhookEnabledEvents = data.EnabledEvents;

            return webhook;
        });

        // Dashboard configuration persistence service
        services.AddSingleton<ConfigPersistenceService>();
        services.AddSingleton<IConfigPersistenceService>(sp => sp.GetRequiredService<ConfigPersistenceService>());

        // Default health check service
        services.AddHostedService<DefaultHealthCheckService>();

        // Startup warmup: pre-initializes SQLite, MemoryCache, and query services
        // to eliminate cold-start latency on the first request.
        services.AddHostedService<StartupWarmupService>();

        // Real-time traffic broadcast via SignalR for topology page.
        services.AddHostedService<TrafficBroadcastService>();

        // JWT secret provider (singleton so it caches the secret after first load)
        services.AddSingleton<JwtSecretProvider>();

        // Route prefix + auth conventions
        services.AddSingleton<IConfigureOptions<MvcOptions>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DashboardOptions>>().Value;
            var prefix = opts.RoutePrefix.Trim('/');

            DashboardController.RoutePrefix = prefix;

            // Use JwtSecretProvider so the secret survives restarts
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
