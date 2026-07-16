using Aneiang.Yarp.Controllers;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.LoadBalancing;

namespace Aneiang.Yarp.Extensions;

public static class AneiangYarpServiceCollectionExtensions
{
    public static IServiceCollection AddAneiangYarp(
        this IServiceCollection services,
        Action<IReverseProxyBuilder>? configureReverseProxy = null,
        bool enableRegistration = true)
    {
        if (services.Any(sd => sd.ServiceType == typeof(AneiangProxyConfigProvider)))
        {
            return services;
        }

        services.AddGatewayApiAuth();
        services.TryAddSingleton(new GatewayControlPlaneOptions { EnableRegistration = enableRegistration });
        services.TryAddSingleton<GatewayApiAuthFilter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<MvcOptions>, GatewayApiAuthMvcOptionsSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, GatewayControlPlaneSecurityValidator>());

        var proxyBuilder = services.AddReverseProxy();

        services.AddSingleton<ILoadBalancingPolicy, IpBasedLoadBalancingPolicy>();

        configureReverseProxy?.Invoke(proxyBuilder);

        services.AddSingleton<AneiangProxyConfigProvider>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var section = config.GetSection("ReverseProxy");
            return new AneiangProxyConfigProvider(
                YarpConfigParser.ParseRoutes(section.GetSection("Routes")),
                YarpConfigParser.ParseClusters(section.GetSection("Clusters")));
        });

        services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<AneiangProxyConfigProvider>());

        services.AddSingleton<DynamicYarpConfigService>();
        services.AddSingleton<IDynamicYarpConfigService>(sp => sp.GetRequiredService<DynamicYarpConfigService>());

        services.AddOptions<BuiltinTransformOptions>()
            .BindConfiguration(BuiltinTransformOptions.SectionName);

        services.AddControllersWithViews()
            .AddApplicationPart(typeof(GatewayConfigController).Assembly);

        services.AddGrpc(options =>
        {
            options.Interceptors.Add<GrpcAuthInterceptor>();
        });
        services.AddSingleton<GrpcAuthInterceptor>();

        if (!enableRegistration)
        {
            services.AddSingleton<IConfigureOptions<MvcOptions>, DisableRegistrationApiMvcOptionsSetup>();
        }

        services.AddAneiangYarpClientInternal();

        return services;
    }

    public static IServiceCollection AddGatewayApiAuth(
        this IServiceCollection services,
        Action<GatewayApiAuthOptions>? configureOptions = null)
    {
        services.AddOptions<ControlPlaneSecurityOptions>()
            .BindConfiguration(ControlPlaneSecurityOptions.SectionName);

        services.AddOptions<GatewayApiAuthOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                var controlPlane = config.GetSection(ControlPlaneSecurityOptions.SectionName).Get<ControlPlaneSecurityOptions>();
                if (controlPlane != null && !string.IsNullOrWhiteSpace(controlPlane.AuthMode))
                {
                    if (Enum.TryParse<GatewayApiAuthMode>(controlPlane.AuthMode, ignoreCase: true, out var mode))
                    {
                        options.Mode = mode;
                        options.Username = controlPlane.Username;
                        options.Password = controlPlane.Password;
                        options.ApiKey = controlPlane.ApiKey;
                        options.ApiKeyHeaderName = string.IsNullOrWhiteSpace(controlPlane.ApiKeyHeaderName) ? "X-Api-Key" : controlPlane.ApiKeyHeaderName;
                        options.AllowApiKeyInQuery = controlPlane.AllowApiKeyInQuery;
                    }
                }

                config.GetSection(GatewayApiAuthOptions.SectionName).Bind(options);

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
            .Configure(configureOptions ?? (_ => { }));

        return services;
    }

}
