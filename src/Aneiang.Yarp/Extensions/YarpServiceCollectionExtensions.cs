using Aneiang.Yarp.Controllers;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Extensions
{
    /// <summary>
    /// Aneiang.Yarp service registration extensions.
    /// <para>Provides three API levels:</para>
    /// <list type="bullet">
    /// <item><b>One-liner</b>: <c>AddAneiangYarpGateway()</c> / <c>AddAneiangYarpClient()</c></item>
    /// <item><b>Customizable</b>: <c>AddAneiangYarpGateway(options => ...)</c></item>
    /// <item><b>Component-level</b>: <c>AddAneiangYarp()</c> / <c>AddAneiangYarpGatewayClient()</c></item>
    /// </list>
    /// </summary>
    public static class YarpServiceCollectionExtensions
    {
        // ═══════════════════════════════════════════
        //  One-liner API: Gateway role (minimum code)
        // ═══════════════════════════════════════════

        /// <summary>
        /// [One-liner — Gateway] Register all services needed for a gateway.
        /// <para>Includes: AddReverseProxy + AddAneiangYarp + AddControllersWithViews + AddHttpClient</para>
        /// </summary>
        /// <example>
        /// <code>
        /// // Program.cs — one-liner gateway setup:
        /// builder.Services.AddAneiangYarpGateway();
        /// </code>
        /// </example>
        public static IServiceCollection AddAneiangYarpGateway(this IServiceCollection services)
        {
            return services.AddAneiangYarpGateway(null);
        }

        /// <summary>
        /// [One-liner + Customizable — Gateway] Register gateway services with custom options.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configureOptions">Customize gateway registration options (e.g. set GatewayUrl to register with an upstream gateway).</param>
        /// <example>
        /// <code>
        /// builder.Services.AddAneiangYarpGateway(options =>
        /// {
        ///     options.GatewayUrl = "http://upstream-gateway:5000";  // Optional: connect to upstream gateway
        /// });
        /// </code>
        /// </example>
        public static IServiceCollection AddAneiangYarpGateway(
            this IServiceCollection services,
            Action<GatewayRegistrationOptions>? configureOptions)
        {
            // Automatically register base services
            services.AddReverseProxy();
            services.AddAneiangYarp();
            services.AddControllersWithViews();
            services.AddHttpClient();

            // Optional: configure auto-registration options (gateway can also register with upstream gateway)
            ConfigureGatewayRegistrationOptions(services, configureOptions);

            return services;
        }

        // ═══════════════════════════════════════════
        //  One-liner API: Client role (minimum code)
        // ═══════════════════════════════════════════

        /// <summary>
        /// [One-liner — Client] Register client services with auto-registration/unregistration.
        /// <para>Includes: AddHttpClient + GatewayAutoRegistrationClient + GatewayRegistrationHostedService</para>
        /// <para>Auto-registers with gateway on startup; auto-unregisters on shutdown.</para>
        /// <para><b>Only mandatory config:</b> GatewayUrl (can be set in code or config file).</para>
        /// </summary>
        /// <example>
        /// <code>
        /// // Program.cs — one-liner client setup:
        /// builder.Services.AddAneiangYarpClient();
        ///
        /// // appsettings.json — only one line required:
        /// // { "GatewayRegistration": { "GatewayUrl": "http://192.168.1.100:5000" } }
        /// </code>
        /// </example>
        public static IServiceCollection AddAneiangYarpClient(this IServiceCollection services)
        {
            return services.AddAneiangYarpClient(null);
        }

        /// <summary>
        /// [One-liner + Customizable — Client] Register client services with custom options.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configureOptions">Custom options (higher priority than config file).</param>
        /// <example>
        /// <code>
        /// builder.Services.AddAneiangYarpClient(options =>
        /// {
        ///     options.GatewayUrl = "http://192.168.1.100:5000";
        ///     options.MatchPath = "/api/users/{**catch-all}";
        ///     options.Order = 10;
        /// });
        /// </code>
        /// </example>
        public static IServiceCollection AddAneiangYarpClient(
            this IServiceCollection services,
            Action<GatewayRegistrationOptions>? configureOptions)
        {
            // Automatically register HttpClient
            services.AddHttpClient();

            // Configure options
            ConfigureGatewayRegistrationOptions(services, configureOptions);

            // Register the client service
            services.AddSingleton<GatewayAutoRegistrationClient>();

            // Register the hosted service (auto: register on start + unregister on stop)
            services.AddHostedService<GatewayRegistrationHostedService>();

            return services;
        }

        // ═══════════════════════════════════════════
        //  Component-level API: Full control
        // ═══════════════════════════════════════════

        /// <summary>
        /// [Component-level — Gateway] Register Aneiang.Yarp core services.
        /// <list type="bullet">
        /// <item>InMemoryConfigProvider (loads initial routes/clusters from IConfiguration)</item>
        /// <item>IProxyConfigProvider (pointing to the same InMemoryConfigProvider instance)</item>
        /// <item>DynamicYarpConfigService (dynamic route management)</item>
        /// <item>GatewayConfigController (API: register/unregister/query routes)</item>
        /// <item>GatewayAutoRegistrationClient (optional connection to upstream gateway)</item>
        /// </list>
        /// <para>! Requires manual registration of services.AddReverseProxy() and services.AddHttpClient() before calling.</para>
        /// </summary>
        public static IServiceCollection AddAneiangYarp(this IServiceCollection services)
        {
            // Register InMemoryConfigProvider — load initial routes and clusters from IConfiguration
            services.AddSingleton<InMemoryConfigProvider>(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var proxySection = configuration.GetSection("ReverseProxy");

                var routes = ParseRoutesFromConfig(proxySection.GetSection("Routes"));
                var clusters = ParseClustersFromConfig(proxySection.GetSection("Clusters"));

                return new InMemoryConfigProvider(routes, clusters);
            });

            // Register as IProxyConfigProvider, pointing to the same InMemoryConfigProvider instance
            services.AddSingleton<IProxyConfigProvider>(sp =>
                sp.GetRequiredService<InMemoryConfigProvider>());

            services.AddSingleton<DynamicYarpConfigService>();

            // Add the controller assembly so MVC can discover controllers in this library
            services.AddMvcCore().AddApplicationPart(typeof(GatewayConfigController).Assembly);

            // Register the gateway auto-registration client (this gateway can also register with an upstream gateway)
            services.AddSingleton<GatewayAutoRegistrationClient>();

            return services;
        }

        /// <summary>
        /// [Component-level — Client] Register only GatewayAutoRegistrationClient.
        /// <para>Use this when you need full control over DI registration order.</para>
        /// <para>For auto-registration/unregistration, use <see cref="AddAneiangYarpClient(IServiceCollection, Action{GatewayRegistrationOptions}?)"/> instead.</para>
        /// </summary>
        public static IServiceCollection AddAneiangYarpGatewayClient(this IServiceCollection services)
        {
            services.AddSingleton<GatewayAutoRegistrationClient>();
            return services;
        }

        // ═══════════════════════════════════════════
        //  Internal: Option configuration
        // ═══════════════════════════════════════════

        private static void ConfigureGatewayRegistrationOptions(
            IServiceCollection services,
            Action<GatewayRegistrationOptions>? configureOptions)
        {
            services.AddOptions<GatewayRegistrationOptions>()
                .Configure<IConfiguration>((options, configuration) =>
                {
                    configuration.GetSection(GatewayRegistrationOptions.SectionName).Bind(options);
                });

            if (configureOptions != null)
                services.Configure(configureOptions);

            // Register option type for later injection via IOptions<T>
            services.AddSingleton<IValidateOptions<GatewayRegistrationOptions>, ValidateNothing>();
        }

        // Placeholder validator (does not validate — silently ignores missing config)
        private sealed class ValidateNothing : IValidateOptions<GatewayRegistrationOptions>
        {
            public ValidateOptionsResult Validate(string? name, GatewayRegistrationOptions options)
                => ValidateOptionsResult.Skip;
        }

        // ═══════════════════════════════════════════
        //  Internal: Config parsing
        // ═══════════════════════════════════════════

        private static List<RouteConfig> ParseRoutesFromConfig(IConfigurationSection routesSection)
        {
            var routes = new List<RouteConfig>();

            foreach (var child in routesSection.GetChildren())
            {
                var match = ParseRouteMatch(child.GetSection("Match"));

                routes.Add(new RouteConfig
                {
                    RouteId = child.Key,
                    ClusterId = child["ClusterId"]!,
                    Match = match!,
                    Order = child["Order"] is { Length: > 0 } o && int.TryParse(o, out var order) ? order : null,
                });
            }

            return routes;
        }

        private static RouteMatch? ParseRouteMatch(IConfigurationSection matchSection)
        {
            if (!matchSection.Exists()) return null;

            var path = matchSection.Value;
            if (!string.IsNullOrEmpty(path))
            {
                return new RouteMatch { Path = path };
            }

            path = matchSection["Path"];
            var methods = matchSection.GetSection("Methods").GetChildren()
                .Select(m => m.Value)
                .Where(v => v != null)
                .Cast<string>()
                .ToArray();

            return new RouteMatch
            {
                Path = path,
                Methods = methods.Length > 0 ? methods : null,
            };
        }

        private static List<ClusterConfig> ParseClustersFromConfig(IConfigurationSection clustersSection)
        {
            var clusters = new List<ClusterConfig>();

            foreach (var child in clustersSection.GetChildren())
            {
                var destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase);

                foreach (var dest in child.GetSection("Destinations").GetChildren())
                {
                    destinations[dest.Key] = new DestinationConfig
                    {
                        Address = dest["Address"]!,
                    };
                }

                clusters.Add(new ClusterConfig
                {
                    ClusterId = child.Key,
                    Destinations = destinations,
                    LoadBalancingPolicy = child["LoadBalancingPolicy"],
                });
            }

            return clusters;
        }
    }
}