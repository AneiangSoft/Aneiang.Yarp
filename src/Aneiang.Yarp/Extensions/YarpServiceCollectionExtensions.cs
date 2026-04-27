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
    /// Aneiang.Yarp 服务注册扩展。
    /// <para>提供三种级别的 API：</para>
    /// <list type="bullet">
    /// <item><b>一键式</b>：<c>AddAneiangYarpGateway()</c> / <c>AddAneiangYarpClient()</c> → 一行搞定</item>
    /// <item><b>可定制</b>：<c>AddAneiangYarpGateway(options => ...)</c> → 一键式 + 自定义</item>
    /// <item><b>精细化</b>：<c>AddAneiangYarp()</c> / <c>AddAneiangYarpGatewayClient()</c> → 完全控制</item>
    /// </list>
    /// </summary>
    public static class YarpServiceCollectionExtensions
    {
        // ════════════════════════════════════════════════════════
        //  一键式 API：网关角色（最少代码）
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐【一键式 - 网关角色】注册网关所需的全部服务。
        /// <para>自动包含：AddReverseProxy + AddAneiangYarp + AddControllersWithViews + AddHttpClient</para>
        /// </summary>
        /// <example>
        /// <code>
        /// // Program.cs — 一行搞定网关：
        /// builder.Services.AddAneiangYarpGateway();
        /// </code>
        /// </example>
        public static IServiceCollection AddAneiangYarpGateway(this IServiceCollection services)
        {
            return services.AddAneiangYarpGateway(null);
        }

        /// <summary>
        /// ⭐【一键式 + 可定制 - 网关角色】注册网关服务并自定义选项。
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configureOptions">自定义网关注册选项（如配置 GatewayUrl 以便本网关也能注册到上级网关）</param>
        /// <example>
        /// <code>
        /// builder.Services.AddAneiangYarpGateway(options =>
        /// {
        ///     options.GatewayUrl = "http://upstream-gateway:5000";  // 可选：连到上级网关
        /// });
        /// </code>
        /// </example>
        public static IServiceCollection AddAneiangYarpGateway(
            this IServiceCollection services,
            Action<GatewayRegistrationOptions>? configureOptions)
        {
            // ── 自动注册基础服务 ──
            services.AddReverseProxy();
            services.AddAneiangYarp();                     // 组件级：动态路由 + Controller
            services.AddControllersWithViews();
            services.AddHttpClient();

            // ── 可选：配置自动注册选项（网关也能连到上级网关） ──
            ConfigureGatewayRegistrationOptions(services, configureOptions);

            return services;
        }

        // ════════════════════════════════════════════════════════
        //  一键式 API：客户端角色（最少代码）
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐【一键式 - 客户端角色】注册客户端服务并启用自动注册/注销。
        /// <para>自动包含：AddHttpClient + GatewayAutoRegistrationClient + GatewayRegistrationHostedService</para>
        /// <para>应用启动时自动注册 → 网关；应用关闭时自动注销 ← 网关。</para>
        /// <para><b>唯一必须配置：</b>GatewayUrl（可在代码或配置文件中设置）</para>
        /// </summary>
        /// <example>
        /// <code>
        /// // Program.cs — 一行搞定客户端：
        /// builder.Services.AddAneiangYarpClient();
        ///
        /// // appsettings.json — 最少只需配置一行：
        /// // { "GatewayRegistration": { "GatewayUrl": "http://192.168.1.100:5000" } }
        /// </code>
        /// </example>
        public static IServiceCollection AddAneiangYarpClient(this IServiceCollection services)
        {
            return services.AddAneiangYarpClient(null);
        }

        /// <summary>
        /// ⭐【一键式 + 可定制 - 客户端角色】注册客户端服务并自定义选项。
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configureOptions">自定义选项（优先级高于配置文件）</param>
        /// <example>
        /// <code>
        /// builder.Services.AddAneiangYarpClient(options =>
        /// {
        ///     options.GatewayUrl = "http://192.168.1.100:5000";
        ///     options.MatchPath = "/api/users/{**catch-all}";    // 只转发特定路径
        ///     options.Order = 10;                                  // 高优先级
        /// });
        /// </code>
        /// </example>
        public static IServiceCollection AddAneiangYarpClient(
            this IServiceCollection services,
            Action<GatewayRegistrationOptions>? configureOptions)
        {
            // ── 自动注册 HttpClient ──
            services.AddHttpClient();

            // ── 配置选项 ──
            ConfigureGatewayRegistrationOptions(services, configureOptions);

            // ── 注册客户端服务 ──
            services.AddSingleton<GatewayAutoRegistrationClient>();

            // ── 注册托管服务（自动：启动注册 + 关闭注销） ──
            services.AddHostedService<GatewayRegistrationHostedService>();

            return services;
        }

        // ════════════════════════════════════════════════════════
        //  精细化 API：组件级别（完全控制）
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// 🔧【精细化 - 网关组件】注册 Aneiang.Yarp 核心服务。
        /// <list type="bullet">
        /// <item>InMemoryConfigProvider（从 IConfiguration 加载初始路由/集群）</item>
        /// <item>IProxyConfigProvider（指向同一实例）</item>
        /// <item>DynamicYarpConfigService（动态路由管理）</item>
        /// <item>GatewayConfigController（API：注册/注销/查询路由）</item>
        /// <item>GatewayAutoRegistrationClient（可选连接上级网关）</item>
        /// </list>
        /// <para>⚠ 调用前需手动注册 services.AddReverseProxy() 和 services.AddHttpClient()</para>
        /// </summary>
        public static IServiceCollection AddAneiangYarp(this IServiceCollection services)
        {
            // 注册 InMemoryConfigProvider，从 IConfiguration 中读取初始路由和集群配置
            services.AddSingleton<InMemoryConfigProvider>(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var proxySection = configuration.GetSection("ReverseProxy");

                var routes = ParseRoutesFromConfig(proxySection.GetSection("Routes"));
                var clusters = ParseClustersFromConfig(proxySection.GetSection("Clusters"));

                return new InMemoryConfigProvider(routes, clusters);
            });

            // 注册为 IProxyConfigProvider，指向同一个 InMemoryConfigProvider 实例
            services.AddSingleton<IProxyConfigProvider>(sp =>
                sp.GetRequiredService<InMemoryConfigProvider>());

            services.AddSingleton<DynamicYarpConfigService>();

            // 添加 Controller 所在程序集，使 MVC 可发现本类库中的控制器
            services.AddMvcCore().AddApplicationPart(typeof(GatewayConfigController).Assembly);

            // 注册网关自动注册客户端（当前网关也可以作为客户端注册到上级网关）
            services.AddSingleton<GatewayAutoRegistrationClient>();

            return services;
        }

        /// <summary>
        /// 🔧【精细化 - 客户端组件】仅注册 GatewayAutoRegistrationClient。
        /// <para>适用于：需要完全控制 DI 注册顺序的高级场景。</para>
        /// <para>如需自动注册/注销，请使用 <see cref="AddAneiangYarpClient(IServiceCollection, Action{GatewayRegistrationOptions}?)"/></para>
        /// </summary>
        public static IServiceCollection AddAneiangYarpGatewayClient(this IServiceCollection services)
        {
            services.AddSingleton<GatewayAutoRegistrationClient>();
            return services;
        }

        // ════════════════════════════════════════════════════════
        //  内部：选项配置
        // ════════════════════════════════════════════════════════

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

            // 注册选项类型以便后续注入 IOptions<T>
            services.AddSingleton<IValidateOptions<GatewayRegistrationOptions>, ValidateNothing>();
        }

        // 占位验证器（不验证，静默忽略缺失的配置项）
        private sealed class ValidateNothing : IValidateOptions<GatewayRegistrationOptions>
        {
            public ValidateOptionsResult Validate(string? name, GatewayRegistrationOptions options)
                => ValidateOptionsResult.Skip;
        }

        // ════════════════════════════════════════════════════════
        //  内部：配置解析
        // ════════════════════════════════════════════════════════

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
