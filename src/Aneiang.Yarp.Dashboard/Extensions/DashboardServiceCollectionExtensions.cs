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
using Aneiang.Yarp.Dashboard.Modules.Alert.Services;
using Aneiang.Yarp.Dashboard.Modules.Alert.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Services;
using Aneiang.Yarp.Dashboard.Modules.Waf.Services;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
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

        // ── Storage backend (IGatewayRepository) ─────────────────────────────────
        services.AddAneiangStorage(services.BuildServiceProvider().GetRequiredService<IConfiguration>());

        // ── Audit log ─────────────────────────────────────────────────────────────
        services.AddSingleton<IConfigChangeAuditLog, ConfigChangeAuditLog>();

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

        // ── Alert service ─────────────────────────────────────────────────────────
        services.AddSingleton<IGatewayAlertService, GatewayAlertService>();
        services.AddSingleton<GatewayAlertService>(sp => (GatewayAlertService)sp.GetRequiredService<IGatewayAlertService>());
        services.AddSingleton<AlertHistoryStore>(sp =>
            new AlertHistoryStore(sp.GetRequiredService<IOptions<DashboardOptions>>().Value.AlertMaxRecords));
        services.AddSingleton<WafEventStore>(sp =>
            new WafEventStore(sp.GetRequiredService<IOptions<DashboardOptions>>().Value.WafMaxEvents));

        // ── Policy service (route + cluster policies via IGatewayRepository) ────
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
        // IGatewayRepository (registered by AddAneiangStorage above) already implements
        // INotificationRepository, so we resolve it from there.
        services.AddSingleton<INotificationRepository>(sp => sp.GetRequiredService<IGatewayRepository>());
        services.AddHttpClient("notification");
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddHostedService<NotificationWarmupService>();

        // ── Webhook settings persistence ─────────────────────────────────────────
        services.AddSingleton<WebhookSettingsPersistenceService>();
        services.AddSingleton<IWebhookSettingsPersistenceService>(sp => sp.GetRequiredService<WebhookSettingsPersistenceService>());

        // ── WAF settings persistence ──────────────────────────────────────────
        services.AddSingleton<WafSettingsPersistenceService>();
        services.AddSingleton<IWafSettingsPersistenceService>(sp => sp.GetRequiredService<WafSettingsPersistenceService>());

        // ── Webhook notification service ──────────────────────────────────────────
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
            opts.WebhookTimeoutSeconds = data.TimeoutSeconds > 0 ? data.TimeoutSeconds : 10;
            opts.WebhookRetryCount = data.RetryCount >= 0 ? data.RetryCount : 1;

            // Apply persisted alert config to DashboardOptions
            try
            {
                if (!string.IsNullOrEmpty(data.AlertConfig))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(data.AlertConfig);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("alertEnabled", out var ae)) opts.AlertEnabled = ae.GetBoolean();
                    if (root.TryGetProperty("alertCircuitBreakerOpen", out var acb)) opts.AlertCircuitBreakerOpen = acb.GetBoolean();
                    if (root.TryGetProperty("alertRetryExhausted", out var are)) opts.AlertRetryExhausted = are.GetBoolean();
                    if (root.TryGetProperty("alertWafBlocks", out var awb)) opts.AlertWafBlocks = awb.GetBoolean();
                    if (root.TryGetProperty("alertProxyErrors", out var ape)) opts.AlertProxyErrors = ape.GetBoolean();
                    if (root.TryGetProperty("alertRateLimitExceeded", out var arle)) opts.AlertRateLimitExceeded = arle.GetBoolean();
                    if (root.TryGetProperty("alertMaxRecords", out var amr) && amr.TryGetInt32(out var amrVal)) opts.AlertMaxRecords = amrVal > 0 ? amrVal : 500;
                    if (root.TryGetProperty("wafMaxEvents", out var wme) && wme.TryGetInt32(out var wmeVal)) opts.WafMaxEvents = wmeVal > 0 ? wmeVal : 1000;
                }
            }
            catch { /* alert config is optional */ }

            return webhook;
        });

        // ── Config persistence service ────────────────────────────────────────────
        services.AddSingleton<ConfigPersistenceService>();
        services.AddSingleton<IConfigPersistenceService>(sp => sp.GetRequiredService<ConfigPersistenceService>());

        // ── Default health check service ──────────────────────────────────────────
        services.AddHostedService<DefaultHealthCheckService>();

        // ── Startup warmup: initializes IGatewayRepository, MemoryCache, and query services ──
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
