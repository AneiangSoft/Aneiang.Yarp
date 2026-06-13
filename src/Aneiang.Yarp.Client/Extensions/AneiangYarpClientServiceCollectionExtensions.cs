using Aneiang.Yarp.GatewayRegistry;
using GatewayGrpc = Aneiang.Yarp.GatewayRegistry.GatewayRegistry;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Grpc.Net.Client;
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
        ConfigureClientInfrastructure(services, configureOptions);
        services.AddSingleton<GatewayAutoRegistrationClient>();
        services.AddHostedService<GatewayRegistrationHostedService>();
        return services;
    }

    /// <summary>
    /// Register client services without the hosted service (used by gateway to support upstream registration).
    /// </summary>
    public static IServiceCollection AddAneiangYarpClientInternal(
        this IServiceCollection services)
    {
        ConfigureClientInfrastructure(services, null);
        services.AddSingleton<GatewayAutoRegistrationClient>();
        return services;
    }

    private static void ConfigureClientInfrastructure(
        IServiceCollection services,
        Action<GatewayRegistrationOptions>? configureOptions)
    {
        services.AddHttpClient();
        ConfigureRegistrationOptions(services, configureOptions);
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GatewayRegistrationOptions>>().Value;
            var gatewayUrl = options.GatewayUrl ?? "http://localhost:5000";

            // gRPC over HTTP requires HTTP/2, but .NET 9 Kestrel does not support
            // HTTP/1.1 + HTTP/2 on the same cleartext port. Use port+1 (dedicated HTTP/2 port).
            // See UseYarpKestrelAutoConfig / KestrelExtensions.TryParseAndConfigure.
            if (Uri.TryCreate(gatewayUrl, UriKind.Absolute, out var gwUri) &&
                gwUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                gatewayUrl = $"{gwUri.Scheme}://{gwUri.Host}:{gwUri.Port + 1}{gwUri.PathAndQuery}";
            }

            // NOTE: Insecure cert validation is used here for dev/demo scenarios
            // with self-signed or ASP.NET dev certs. In production, use proper certificate setup instead.
            var handler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback =
                        (sender, cert, chain, errors) => true
                }
            };

            return GrpcChannel.ForAddress(gatewayUrl, new GrpcChannelOptions
            {
                HttpHandler = handler
            });
        });
        services.AddSingleton(sp =>
        {
            var channel = sp.GetRequiredService<GrpcChannel>();
            return new GatewayGrpc.GatewayRegistryClient(channel);
        });
        services.AddSingleton<KestrelAutoConfigService>();
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

        services.AddSingleton<IValidateOptions<GatewayRegistrationOptions>, SkipValidation>();
    }

    /// <summary>Skips options validation (all config is optional).</summary>
    internal sealed class SkipValidation : IValidateOptions<GatewayRegistrationOptions>
    {
        public ValidateOptionsResult Validate(string? name, GatewayRegistrationOptions options)
            => ValidateOptionsResult.Skip;
    }
}
