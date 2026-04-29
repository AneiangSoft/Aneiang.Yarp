using System.Security.Cryptography;
using Aneiang.Yarp.Dashboard.Controllers;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<ProxyLogStore>();
        services.AddSingleton<YarpEventSourceListener>();
        services.AddHostedService<YarpEventSourceListenerStartupService>();

        // Register downstream capture transform (runs after all other transforms)
        services.AddSingleton<ITransformProvider, DownstreamCaptureTransformProvider>();

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

                var authFilter = CreateAuthFilter(opts, prefix);
                if (authFilter != null)
                    mvcOptions.Filters.Add(authFilter);
            });
        });

        return services;
    }

    // -- Auth filter factory

    /// <summary>Creates an auth filter based on the configured auth mode.</summary>
    private static DashboardAuthFilter? CreateAuthFilter(DashboardOptions opts, string routePrefix)
    {
        // Priority 1: Custom delegate (highest)
        if (opts.AuthorizeRequest != null)
            return new DashboardAuthFilter(ctx => opts.AuthorizeRequest(ctx), routePrefix);

        // Priority 2: API Key
        if (opts.AuthMode == DashboardAuthMode.ApiKey && !string.IsNullOrEmpty(opts.ApiKey))
        {
            var apiKey = opts.ApiKey;
            var headerName = opts.ApiKeyHeaderName;
            return new DashboardAuthFilter(ctx =>
            {
                if (ctx.Request.Headers.TryGetValue(headerName, out var hv) && hv.Any(v => v == apiKey))
                    return Task.FromResult(true);
                if (ctx.Request.Query.TryGetValue("api-key", out var qv) && qv.Any(v => v == apiKey))
                    return Task.FromResult(true);
                return Task.FromResult(false);
            }, routePrefix);
        }

        // Priority 3: JWT (DefaultJwt / CustomJwt)
        if (opts.AuthMode is DashboardAuthMode.CustomJwt or DashboardAuthMode.DefaultJwt)
        {
            var secret = opts.JwtSecret!;
            return new DashboardAuthFilter(ctx =>
            {
                // Authorization header (for XHR/API calls)
                var authHeader = ctx.Request.Headers["Authorization"].FirstOrDefault();
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                {
                    var (valid, _) = DashboardJwtHelper.ValidateToken(authHeader[7..], secret);
                    if (valid) return Task.FromResult(true);
                }

                // dashboard_token cookie (for browser page loads)
                if (ctx.Request.Cookies.TryGetValue("dashboard_token", out var cookieToken)
                    && !string.IsNullOrEmpty(cookieToken))
                {
                    var (valid, _) = DashboardJwtHelper.ValidateToken(cookieToken, secret);
                    return Task.FromResult(valid);
                }

                return Task.FromResult(false);
            }, routePrefix);
        }

        // AuthMode.None with no custom delegate - no filter
        return null;
    }
}
