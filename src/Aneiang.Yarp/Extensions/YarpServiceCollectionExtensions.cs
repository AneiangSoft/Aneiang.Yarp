using System.Text;
using Aneiang.Yarp.Controllers;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Extensions;

/// <summary>
/// Aneiang.Yarp service registration extensions / 服务注册扩展方法.
/// </summary>
public static class YarpServiceCollectionExtensions
{
    // ── Gateway 网关 ──────────────────────────────────────────

    /// <summary>
    /// Register the YARP gateway with full pipeline support / 注册 YARP 网关并加载全量管道.
    /// Automatically loads routes/clusters from <c>ReverseProxy</c> config section and enables dynamic config updates.
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <param name="configureReverseProxy">Optional YARP pipeline customization (transforms, service discovery, etc.) / 可选的 YARP 管道定制</param>
    public static IServiceCollection AddAneiangYarp(
        this IServiceCollection services,
        Action<IReverseProxyBuilder>? configureReverseProxy = null)
    {
        var proxyBuilder = services.AddReverseProxy();
        configureReverseProxy?.Invoke(proxyBuilder);

        // InMemoryConfigProvider: load static routes/clusters from IConfiguration
        services.AddSingleton<InMemoryConfigProvider>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var section = config.GetSection("ReverseProxy");
            return new InMemoryConfigProvider(
                ParseRoutes(section.GetSection("Routes")),
                ParseClusters(section.GetSection("Clusters")));
        });

        // Sole config provider — both static + dynamic
        services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<InMemoryConfigProvider>());
        services.AddSingleton<DynamicYarpConfigService>();

        // Register controllers + views so this library's controllers/MVC are discoverable
        services.AddControllersWithViews()
            .AddApplicationPart(typeof(GatewayConfigController).Assembly);

        // Registration client (gateway can itself register with an upstream gateway)
        services.AddSingleton<GatewayAutoRegistrationClient>();
        services.AddHttpClient();

        return services;
    }

    // ── Client 客户端自动注册 ──────────────────────────────────

    /// <summary>
    /// Register this service as a YARP client with auto-registration to the gateway / 注册为客户端并自动向网关注册路由.
    /// Auto-registers route on startup, auto-unregisters on shutdown.
    /// The only required config is <c>GatewayUrl</c> (code or appsettings.json).
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <param name="configureOptions">Optional override for registration options / 可选的注册选项覆盖</param>
    /// <example>
    /// <code>
    /// // Minimal — only config file needed:
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
        ConfigureRegistrationOptions(services, configureOptions);
        services.AddSingleton<GatewayAutoRegistrationClient>();
        services.AddHostedService<GatewayRegistrationHostedService>();
        return services;
    }

    // ── Gateway API 授权 ──────────────────────────────────────

    /// <summary>
    /// Enable authorization for the gateway config API (register-route, delete-route, etc.) / 启用网关配置 API 的授权保护.
    /// Auto-reads credentials from <c>Gateway:Dashboard</c> when Dashboard is configured.
    /// Supports BasicAuth and ApiKey modes.
    /// </summary>
    /// <example>
    /// <code>
    /// // appsettings.json — one config section for both Dashboard and API auth:
    /// { "Gateway": { "Dashboard": { "AuthMode": "DefaultJwt", "JwtPassword": "Ads@2026" } } }
    ///
    /// // Program.cs:
    /// builder.Services.AddAneiangYarp()
    ///               .AddAneiangYarpDashboard()
    ///               .AddGatewayApiAuth();
    /// </code>
    /// </example>
    public static IServiceCollection AddGatewayApiAuth(this IServiceCollection services)
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
            });

        services.AddSingleton<GatewayApiAuthFilter>();
        services.AddSingleton<IConfigureOptions<MvcOptions>>(_ =>
            new ConfigureNamedOptions<MvcOptions>(null, mvo =>
                mvo.Conventions.Add(new GatewayApiAuthConvention())));

        return services;
    }

    // ═══════════════════════════════════════════════════════════
    //  Internal helpers / 内部辅助
    // ═══════════════════════════════════════════════════════════

    private static void ConfigureRegistrationOptions(
        IServiceCollection services,
        Action<GatewayRegistrationOptions>? configureOptions)
    {
        services.AddOptions<GatewayRegistrationOptions>()
            .Configure<IConfiguration>((o, c) =>
                c.GetSection(GatewayRegistrationOptions.SectionName).Bind(o));

        if (configureOptions != null)
            services.Configure(configureOptions);

        // Suppress validation — options are optional
        services.AddSingleton<IValidateOptions<GatewayRegistrationOptions>, SkipValidation>();
    }

    /// <summary>Skips options validation (all config is optional).</summary>
    private sealed class SkipValidation : IValidateOptions<GatewayRegistrationOptions>
    {
        public ValidateOptionsResult Validate(string? name, GatewayRegistrationOptions options)
            => ValidateOptionsResult.Skip;
    }

    /// <summary>Applies GatewayApiAuthFilter to GatewayConfigController only.</summary>
    private sealed class GatewayApiAuthConvention : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application)
        {
            var ctrl = application.Controllers.FirstOrDefault(c => c.ControllerType == typeof(GatewayConfigController));
            if (ctrl == null) return;
            ctrl.Filters.Add(new ServiceFilterAttribute(typeof(GatewayApiAuthFilter)) { IsReusable = true });
        }
    }

    /// <summary>Authorization filter: supports BasicAuth and ApiKey modes / 授权过滤器：支持 BasicAuth 和 ApiKey 模式.</summary>
    private sealed class GatewayApiAuthFilter : IAsyncAuthorizationFilter
    {
        private readonly GatewayApiAuthOptions _opts;
        public GatewayApiAuthFilter(IOptions<GatewayApiAuthOptions> options) => _opts = options.Value;

        public Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
        {
            var req = ctx.HttpContext.Request;
            var ok = _opts.Mode switch
            {
                GatewayApiAuthMode.None => true,
                GatewayApiAuthMode.BasicAuth => ValidateBasic(req),
                GatewayApiAuthMode.ApiKey => ValidateApiKey(req),
                _ => false
            };

            if (!ok)
                ctx.Result = new JsonResult(new { code = 401, message = "Unauthorized" }) { StatusCode = 401 };

            return Task.CompletedTask;
        }

        private bool ValidateBasic(HttpRequest req)
        {
            if (string.IsNullOrWhiteSpace(_opts.Username) || string.IsNullOrWhiteSpace(_opts.Password))
                return false;

            var h = req.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(h) || !h.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(h[6..]));
                var idx = decoded.IndexOf(':');
                return idx >= 0
                    && decoded[..idx] == _opts.Username
                    && decoded[(idx + 1)..] == _opts.Password;
            }
            catch { return false; }
        }

        private bool ValidateApiKey(HttpRequest req)
        {
            if (string.IsNullOrWhiteSpace(_opts.ApiKey)) return false;

            // Header first
            if (req.Headers.TryGetValue(_opts.ApiKeyHeaderName, out var hv)
                && hv.Any(v => v == _opts.ApiKey))
                return true;

            // Query fallback
            return req.Query.TryGetValue("api-key", out var qv) && qv.Any(v => v == _opts.ApiKey);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Config parsing / 配置解析
    // ═══════════════════════════════════════════════════════════

    private static List<RouteConfig> ParseRoutes(IConfigurationSection section)
    {
        var routes = new List<RouteConfig>();
        foreach (var child in section.GetChildren())
        {
            var transforms = ParseTransforms(child.GetSection("Transforms"));
            routes.Add(new RouteConfig
            {
                RouteId = child.Key,
                ClusterId = child["ClusterId"]!,
                Match = ParseMatch(child.GetSection("Match"))!,
                Order = child["Order"] is { Length: > 0 } o && int.TryParse(o, out var order) ? order : null,
                Transforms = transforms
            });
        }
        return routes;
    }

    private static RouteMatch? ParseMatch(IConfigurationSection section)
    {
        if (!section.Exists()) return null;

        // Short form: "Path": "/api/{**catch-all}"
        var path = section.Value;
        if (!string.IsNullOrEmpty(path))
            return new RouteMatch { Path = path };

        // Full form: { "Path": "...", "Methods": ["GET", "POST"] }
        var methods = section.GetSection("Methods").GetChildren()
            .Select(m => m.Value).Where(v => v != null).Cast<string>().ToArray();

        return new RouteMatch
        {
            Path = section["Path"],
            Methods = methods.Length > 0 ? methods : null
        };
    }

    private static List<ClusterConfig> ParseClusters(IConfigurationSection section)
    {
        var clusters = new List<ClusterConfig>();
        foreach (var child in section.GetChildren())
        {
            var destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var dest in child.GetSection("Destinations").GetChildren())
                destinations[dest.Key] = new DestinationConfig { Address = dest["Address"]! };

            clusters.Add(new ClusterConfig
            {
                ClusterId = child.Key,
                Destinations = destinations,
                LoadBalancingPolicy = child["LoadBalancingPolicy"]
            });
        }
        return clusters;
    }

    private static List<Dictionary<string, string>>? ParseTransforms(IConfigurationSection section)
    {
        if (!section.Exists()) return null;

        var list = new List<Dictionary<string, string>>();
        foreach (var t in section.GetChildren())
        {
            var dict = new Dictionary<string, string>();
            foreach (var entry in t.GetChildren())
                if (entry.Value != null) dict[entry.Key] = entry.Value;
            if (dict.Count > 0) list.Add(dict);
        }
        return list.Count > 0 ? list : null;
    }
}