using Aneiang.Yarp.Controllers;
using Aneiang.Yarp.Middleware;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.LoadBalancing;

namespace Aneiang.Yarp.Extensions;

/// <summary>
/// Aneiang.Yarp service registration extensions.
/// </summary>
public static class AneiangYarpServiceCollectionExtensions
{
    // -- Gateway

    /// <summary>
    /// Register the YARP gateway with full pipeline support.
    /// Automatically loads routes/clusters from <c>ReverseProxy</c> config section and enables dynamic config updates.
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <param name="configureReverseProxy">Optional YARP pipeline customization (transforms, service discovery, etc.).</param>
    /// <param name="enableRegistration">
    /// Whether to expose the route registration API (<c>GatewayConfigController</c>).
    /// Set to <c>false</c> when this gateway should not accept external route registration requests.
    /// Default: <c>true</c>.
    /// </param>
    public static IServiceCollection AddAneiangYarp(
        this IServiceCollection services,
        Action<IReverseProxyBuilder>? configureReverseProxy = null,
        bool enableRegistration = true)
    {
        // Guard: prevent double registration. AddReverseProxy() is idempotent (TryAdd* internally),
        // but repeated calls to this method would re-register InMemoryConfigProvider / IProxyConfigProvider
        // unnecessarily and trigger confusing double behavior.
        if (services.Any(sd => sd.ServiceType == typeof(InMemoryConfigProvider)))
        {
            return services;
        }

        var proxyBuilder = services.AddReverseProxy();

        // Register custom load balancing policies
        services.AddSingleton<ILoadBalancingPolicy, IpBasedLoadBalancingPolicy>();

        configureReverseProxy?.Invoke(proxyBuilder);

        // InMemoryConfigProvider: load static routes/clusters from IConfiguration
        services.AddSingleton<InMemoryConfigProvider>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var section = config.GetSection("ReverseProxy");
            return new InMemoryConfigProvider(
                YarpConfigParser.ParseRoutes(section.GetSection("Routes")),
                YarpConfigParser.ParseClusters(section.GetSection("Clusters")));
        });

        // Sole config provider - both static + dynamic
        services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<InMemoryConfigProvider>());

        // Dynamic config service (depends on IDynamicConfigPersistenceService and IConfigChangeAuditLog
        // which are registered by Dashboard when AddAneiangYarpDashboard is called)
        services.AddSingleton<DynamicYarpConfigService>();
        services.AddSingleton<IDynamicYarpConfigService>(sp => sp.GetRequiredService<DynamicYarpConfigService>());

        // Built-in transform options
        services.AddOptions<BuiltinTransformOptions>()
            .BindConfiguration(BuiltinTransformOptions.SectionName);

        // Register controllers + views so this library's controllers/MVC are discoverable
        services.AddControllersWithViews()
            .AddApplicationPart(typeof(GatewayConfigController).Assembly);

        services.AddGrpc();

        // Remove registration API endpoints when disabled (security: no route = 404, not 401/403)
        if (!enableRegistration)
        {
            services.AddSingleton<IConfigureOptions<MvcOptions>>(_ =>
                new ConfigureNamedOptions<MvcOptions>(null, mvo =>
                    mvo.Conventions.Add(new DisableRegistrationApiConvention())));
        }

        // Registration client (gateway can itself register with an upstream gateway)
        services.AddAneiangYarpClientInternal();

        return services;
    }

    // -- Gateway API authorization

    /// <summary>
    /// Enable authorization for the gateway config API (register-route, delete-route, etc.).
    /// Supports BasicAuth and ApiKey modes.
    /// Credentials are resolved in this order (later overrides earlier):
    /// 1. <c>Gateway:ApiAuth</c> config section
    /// 2. Auto-detect from <c>Gateway:Dashboard</c> (if Dashboard JWT password is configured)
    /// 3. <paramref name="configureOptions"/> callback (highest precedence)
    /// <para>
    /// <b>Note:</b> The actual <c>GatewayApiAuthFilter</c> and <c>GatewayApiAuthConvention</c>
    /// are registered by <c>AddAneiangYarpDashboard</c> when the Dashboard package is used.
    /// When using only the core package, call this method to bind options and then manually
    /// register the filter/convention in your own composition root.
    /// </para>
    /// </summary>
    public static IServiceCollection AddGatewayApiAuth(
        this IServiceCollection services,
        Action<GatewayApiAuthOptions>? configureOptions = null)
    {
        services.AddOptions<GatewayApiAuthOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                // 1. Try explicit Gateway:ApiAuth section
                config.GetSection(GatewayApiAuthOptions.SectionName).Bind(options);

                // 2. If still None, auto-detect from Gateway:Dashboard
                if (options.Mode == GatewayApiAuthMode.None)
                {
                    var dash = config.GetSection("Gateway:Dashboard");
                    var pwd = dash["JwtPassword"];
                    if (!string.IsNullOrWhiteSpace(pwd))
                    {
                        options.Mode = GatewayApiAuthMode.BasicAuth;
                        options.Password = pwd;
                        options.Username = string.Equals(dash["AuthMode"], "CustomJwt", StringComparison.OrdinalIgnoreCase)
                            ? dash["JwtUsername"] ?? "admin"
                            : "admin";
                    }
                }
            })
            // 3. User callback overrides all config sources (highest precedence)
            .Configure(configureOptions ?? (_ => { }));

        return services;
    }

}
