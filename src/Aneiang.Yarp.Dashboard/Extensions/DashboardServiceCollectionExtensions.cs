using System.Security.Cryptography;
using Aneiang.Yarp.Dashboard.Controllers;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Aneiang.Yarp.Middleware;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;
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
    /// <param name="services">IServiceCollection</param>
    /// <param name="configureOptions">Optional override for dashboard options.</param>
    /// <example>
    /// <code>
    /// // Minimal setup with JWT auth:
    /// services.AddAneiangYarpDashboard(o =>
    /// {
    ///     o.AuthMode = DashboardAuthMode.DefaultJwt;
    ///     o.JwtPassword = "Ads@2026";
    /// });
    ///
    /// // Or via config file:
    /// { "Gateway": { "Dashboard": { "AuthMode": "DefaultJwt", "JwtPassword": "Ads@2026" } } }
    /// </code>
    /// </example>
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

        // YARP log capture (zero dependency on logging frameworks)
        services.AddSingleton<IProxyLogStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DashboardOptions>>().Value;
            return new ProxyLogStore(opts.LogBufferCapacity);
        });
        services.AddSingleton<ProxyLogStore>(sp => (ProxyLogStore)sp.GetRequiredService<IProxyLogStore>());
        services.AddSingleton<LogSanitizer>();
        services.AddSingleton<YarpEventSourceListener>();
        services.AddHostedService<YarpEventSourceListenerStartupService>();

        // Register downstream capture transform (runs after all other transforms)
        services.AddSingleton<ITransformProvider, DownstreamCaptureTransformProvider>();

        // Register dashboard query services
        services.AddSingleton<IDashboardInfoQueryService, DashboardInfoQueryService>();
        services.AddSingleton<IDashboardClusterQueryService, DashboardClusterQueryService>();
        services.AddSingleton<IDashboardRouteQueryService, DashboardRouteQueryService>();
        services.AddSingleton<IDashboardLogQueryService, DashboardLogQueryService>();

        // Register editable policy
        services.AddSingleton<IEditablePolicy, DashboardEditablePolicy>();

        // Register authorization service
        services.AddSingleton<IDashboardAuthorizationService, DashboardAuthorizationService>();

        // Register webhook notification service
        services.AddHttpClient("webhook");
        services.AddSingleton<WebhookNotificationService>(sp =>
        {
            var webhook = new WebhookNotificationService(
                sp.GetRequiredService<IOptions<DashboardOptions>>(),
                sp.GetRequiredService<ILogger<WebhookNotificationService>>(),
                sp.GetRequiredService<IHttpClientFactory>());
            webhook.Subscribe(sp.GetRequiredService<ConfigChangeAuditLog>());
            return webhook;
        });

        // Register configuration persistence service
        services.AddSingleton(sp =>
        {
            var filePersistence = sp.GetRequiredService<DynamicConfigPersistenceService>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ConfigPersistenceService>>();
            var dynamicConfig = sp.GetService<DynamicYarpConfigService>();
            return new ConfigPersistenceService(filePersistence, logger, dynamicConfig);
        });

        // Register dynamic config persistence service (from Aneiang.Yarp)
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DynamicConfigPersistenceService>>();
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gateway-dynamic.json");
            return new DynamicConfigPersistenceService(configPath, logger);
        });

        // Route prefix + auth conventions
        services.AddSingleton<IConfigureOptions<MvcOptions>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DashboardOptions>>().Value;
            var prefix = opts.RoutePrefix.Trim('/');

            // Share config with controller
            DashboardController.RoutePrefix = prefix;

            // Auto-generate JWT secret if not set
            if (opts.JwtSecret == null && opts.AuthMode is DashboardAuthMode.CustomJwt or DashboardAuthMode.DefaultJwt)
            {
                var randomBytes = new byte[32];
                RandomNumberGenerator.Fill(randomBytes);
                opts.JwtSecret = Convert.ToBase64String(randomBytes);
            }

            return new ConfigureNamedOptions<MvcOptions>(null, mvcOptions =>
            {
                mvcOptions.Conventions.Add(new DashboardRouteConvention(prefix));

                // Add auth filter if authorization is enabled
                var authFilter = CreateAuthFilter(opts, prefix);
                if (authFilter != null)
                    mvcOptions.Filters.Add(authFilter);
            });
        });

        return services;
    }

    // -- Auth filter factory

    /// <summary>Creates an auth filter if authorization is enabled.</summary>
    private static DashboardAuthFilter? CreateAuthFilter(DashboardOptions opts, string routePrefix)
    {
        // No authorization needed for AuthMode.None without custom delegate
        if (opts.AuthMode == DashboardAuthMode.None && opts.AuthorizeRequest == null)
            return null;

        // Create a temporary auth service instance for the filter
        // The actual service will be resolved from DI at runtime
        return new DashboardAuthFilter(
            new DashboardAuthorizationService(Microsoft.Extensions.Options.Options.Create(opts)),
            routePrefix);
    }
}
