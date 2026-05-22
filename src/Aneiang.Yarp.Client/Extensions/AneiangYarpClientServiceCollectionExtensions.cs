using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Extensions;

/// <summary>
/// Aneiang.Yarp client service registration extensions.
/// </summary>
public static class AneiangYarpClientServiceCollectionExtensions
{
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

    // Internal helpers

    /// <summary>
    /// Register client services without the hosted service (used by gateway to support upstream registration).
    /// </summary>
    public static IServiceCollection AddAneiangYarpClientInternal(
        this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<KestrelAutoConfigService>();
        ConfigureRegistrationOptions(services, null);
        services.AddSingleton<GatewayAutoRegistrationClient>();
        return services;
    }

    internal static void ConfigureRegistrationOptions(
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
    internal sealed class SkipValidation : IValidateOptions<GatewayRegistrationOptions>
    {
        public ValidateOptionsResult Validate(string? name, GatewayRegistrationOptions options)
            => ValidateOptionsResult.Skip;
    }
}
