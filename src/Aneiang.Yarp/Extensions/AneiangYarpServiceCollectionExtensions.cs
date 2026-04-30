using Aneiang.Yarp.Controllers;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Configuration;

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
        var proxyBuilder = services.AddReverseProxy();
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
        
        // Dynamic config persistence service
        services.AddSingleton<DynamicConfigPersistenceService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var dataDir = config["Gateway:DynamicConfigPath"] ?? "gateway-dynamic.json";
            var logger = sp.GetRequiredService<ILogger<DynamicConfigPersistenceService>>();
            return new DynamicConfigPersistenceService(dataDir, logger);
        });
        
        services.AddSingleton<DynamicYarpConfigService>();

        // Register controllers + views so this library's controllers/MVC are discoverable
        services.AddControllersWithViews()
            .AddApplicationPart(typeof(GatewayConfigController).Assembly);

        // Remove registration API endpoints when disabled (security: no route = 404, not 401/403)
        if (!enableRegistration)
        {
            services.AddSingleton<IConfigureOptions<MvcOptions>>(_ =>
                new ConfigureNamedOptions<MvcOptions>(null, mvo =>
                    mvo.Conventions.Add(new DisableRegistrationApiConvention())));
        }

        // Registration client (gateway can itself register with an upstream gateway)
        services.AddSingleton<KestrelAutoConfigService>();
        services.AddSingleton<GatewayAutoRegistrationClient>();
        services.AddHttpClient();

        return services;
    }

    // -- Client auto-registration

    /// <summary>
    /// Register this service as a YARP client with auto-registration to the gateway.
    /// Auto-registers route on startup, auto-unregisters on shutdown.
    /// The only required config is <c>GatewayUrl</c> (code or appsettings.json).
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <param name="configureOptions">Optional override for registration options.</param>
    /// <example>
    /// <code>
    /// // Minimal - only config file needed:
    /// builder.Services.AddAneiangYarpClient();
    /// // { "Gateway:Registration": { "GatewayUrl": "http://192.168.1.100:5000" } }
    ///
    /// // With code override:
    /// builder.Services.AddAneiangYarpClient(o => {
    ///     o.GatewayUrl = "http://gateway:5000";
    ///     o.MatchPath = "/api/users/{**catch-all}";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddAneiangYarpClient(
        this IServiceCollection services,
        Action<GatewayRegistrationOptions>? configureOptions = null)
    {
        services.AddHttpClient();
        services.AddSingleton<KestrelAutoConfigService>();
        ConfigureRegistrationOptions(services, configureOptions);
        services.AddSingleton<GatewayAutoRegistrationClient>();
        services.AddHostedService<GatewayRegistrationHostedService>();
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
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <param name="configureOptions">Optional manual override for auth options. Takes precedence over all config sources.</param>
    /// <example>
    /// <code>
    /// // From config file:
    /// builder.Services.AddGatewayApiAuth();
    ///
    /// // From code (highest precedence):
    /// builder.Services.AddGatewayApiAuth(o => {
    ///     o.Mode = GatewayApiAuthMode.BasicAuth;
    ///     o.Username = "admin";
    ///     o.Password = "admin@2026";
    /// });
    /// </code>
    /// </example>
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

        services.AddSingleton<GatewayApiAuthFilter>();
        services.AddSingleton<IConfigureOptions<MvcOptions>>(_ =>
            new ConfigureNamedOptions<MvcOptions>(null, mvo =>
                mvo.Conventions.Add(new GatewayApiAuthConvention())));

        return services;
    }

    // Internal helpers

    private static void ConfigureRegistrationOptions(
        IServiceCollection services,
        Action<GatewayRegistrationOptions>? configureOptions)
    {
        services.AddOptions<GatewayRegistrationOptions>()
            .Configure<IConfiguration>((o, c) =>
                c.GetSection(GatewayRegistrationOptions.SectionName).Bind(o));

        if (configureOptions != null)
            services.Configure(configureOptions);

        // Suppress validation - options are optional
        services.AddSingleton<IValidateOptions<GatewayRegistrationOptions>, SkipValidation>();
    }

    /// <summary>Skips options validation (all config is optional).</summary>
    private sealed class SkipValidation : IValidateOptions<GatewayRegistrationOptions>
    {
        public ValidateOptionsResult Validate(string? name, GatewayRegistrationOptions options)
            => ValidateOptionsResult.Skip;
    }
}
