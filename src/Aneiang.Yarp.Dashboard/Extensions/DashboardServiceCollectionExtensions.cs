using System.Security.Cryptography;
using Aneiang.Yarp.Dashboard.Controllers;
using Aneiang.Yarp.Dashboard.Middleware;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Services.Implements;
using Aneiang.Yarp.Dashboard.Services.Webhook;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

        if (configureOptions != null)
            services.Configure(configureOptions);

        // Register MVC controllers from this assembly
        services.AddMvcCore().AddApplicationPart(typeof(DashboardController).Assembly);

        // ── Dashboard-specific services (moved from Aneiang.Yarp) ──────────────

        // Override core service implementations with Dashboard implementations
        services.AddSingleton<IConfigChangeAuditLog, ConfigChangeAuditLog>();
        services.AddSingleton<IDynamicConfigPersistenceService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var configPath = config["Gateway:DynamicConfigPath"] ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gateway-dynamic.json");
            var logger = sp.GetRequiredService<ILogger<DynamicConfigPersistenceService>>();
            return new DynamicConfigPersistenceService(configPath, logger);
        });

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

        // Authorization service
        services.AddSingleton<IDashboardAuthorizationService, DashboardAuthorizationService>();

        // Webhook notification service
        services.AddHttpClient("webhook");
        services.AddSingleton<IWebhookProvider, DingTalkWebhookProvider>();
        services.AddSingleton<IWebhookProvider, GenericWebhookProvider>();
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<WebhookSettingsPersistenceService>>();
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webhook-settings.json");
            return new WebhookSettingsPersistenceService(configPath, logger);
        });
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
        services.AddSingleton(sp =>
        {
            var filePersistence = sp.GetRequiredService<IDynamicConfigPersistenceService>();
            var logger = sp.GetRequiredService<ILogger<ConfigPersistenceService>>();
            var dynamicConfig = sp.GetService<DynamicYarpConfigService>();
            return new ConfigPersistenceService(filePersistence, logger, dynamicConfig);
        });

        // Default health check service
        services.AddHostedService<DefaultHealthCheckService>();

        // Route prefix + auth conventions
        services.AddSingleton<IConfigureOptions<MvcOptions>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DashboardOptions>>().Value;
            var prefix = opts.RoutePrefix.Trim('/');

            DashboardController.RoutePrefix = prefix;

            if (opts.JwtSecret == null && opts.AuthMode is DashboardAuthMode.CustomJwt or DashboardAuthMode.DefaultJwt)
            {
                var randomBytes = new byte[32];
                RandomNumberGenerator.Fill(randomBytes);
                opts.JwtSecret = Convert.ToBase64String(randomBytes);
            }

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
            new DashboardAuthorizationService(Microsoft.Extensions.Options.Options.Create(opts)),
            routePrefix);
    }
}
