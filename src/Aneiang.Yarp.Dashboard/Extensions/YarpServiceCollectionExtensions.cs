using System.Security.Cryptography;
using Aneiang.Yarp.Dashboard.Controllers;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>Aneiang.Yarp.Dashboard service registration extensions / Dashboard 服务注册扩展.</summary>
public static class YarpServiceCollectionExtensions
{
    /// <summary>
    /// Register Dashboard with configurable auth and route prefix / 注册 Dashboard 可配置授权和路由前缀.
    /// Options are bound from <c>Gateway:Dashboard</c> configuration section.
    /// </summary>
    /// <param name="services">IServiceCollection</param>
    /// <param name="configureOptions">Optional override for dashboard options / 可选的选项覆盖.</param>
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
        // EventSource: captures YARP forwarder events (stages, status, bytes)
        // Middleware: captures request path/method before YARP processing
        services.AddSingleton<ProxyLogStore>();
        services.AddSingleton<YarpEventSourceListener>();
        services.AddHostedService<YarpEventSourceListenerStartupService>();

        // Register downstream capture transform (runs after all other transforms)
        // Captures the actual request body YARP sends downstream (encrypted, etc.)
        services.AddSingleton<ITransformProvider, DownstreamCaptureTransformProvider>();

        // Route prefix + auth conventions
        services.AddSingleton<IConfigureOptions<MvcOptions>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DashboardOptions>>().Value;
            var prefix = opts.RoutePrefix.Trim('/');

            // Share config with controller
            DashboardController.RoutePrefix = prefix;
            DashboardController.Options = opts;

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

                var authFilter = CreateAuthFilter(opts);
                if (authFilter != null)
                    mvcOptions.Filters.Add(authFilter);
            });
        });

        return services;
    }

    // ── Route convention / 路由约定 ─────────────────────────────

    /// <summary>Prepends the dashboard route prefix to all DashboardController actions / 为所有 action 添加路由前缀.</summary>
    private sealed class DashboardRouteConvention : IApplicationModelConvention
    {
        private readonly string _prefix;
        public DashboardRouteConvention(string prefix) => _prefix = prefix;

        public void Apply(ApplicationModel application)
        {
            var ctrl = application.Controllers.FirstOrDefault(c => c.ControllerType == typeof(DashboardController));
            if (ctrl == null) return;

            foreach (var action in ctrl.Actions)
            {
                foreach (var selector in action.Selectors)
                {
                    if (selector.AttributeRouteModel == null) continue;
                    var template = selector.AttributeRouteModel.Template ?? "";
                    selector.AttributeRouteModel.Template = template.StartsWith("/")
                        ? _prefix + template
                        : _prefix + "/" + template;
                }
            }
        }
    }

    // ── Auth filter factory / 授权过滤器工厂 ─────────────────────

    /// <summary>Creates an auth filter based on the configured auth mode / 根据配置的认证模式创建授权过滤器.</summary>
    private static DashboardAuthFilter? CreateAuthFilter(DashboardOptions opts)
    {
        // Priority 1: Custom delegate (highest) / 自定义委托（最高优先级）
        if (opts.AuthorizeRequest != null)
            return new DashboardAuthFilter(ctx => opts.AuthorizeRequest(ctx));

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
            });
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
            });
        }

        // AuthMode.None with no custom delegate — no filter
        return null;
    }

    // ── Authorization filter / 授权过滤器 ────────────────────────

    /// <summary>
    /// Async auth filter for DashboardController.
    /// Skips login actions. Redirects browser to login; returns 401 JSON for API calls.
    /// 异步授权过滤器：跳过登录页，浏览器跳转登录，API 返回 401.
    /// </summary>
    private sealed class DashboardAuthFilter : IAsyncAuthorizationFilter
    {
        private readonly Func<HttpContext, Task<bool>> _checkAsync;
        public DashboardAuthFilter(Func<HttpContext, Task<bool>> checkAsync) => _checkAsync = checkAsync;

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            // Only apply to DashboardController actions
            if (context.ActionDescriptor is not ControllerActionDescriptor
                { ControllerTypeInfo: var ci } || ci.AsType() != typeof(DashboardController))
                return;

            // Skip login actions — they are public
            var actionName = ((ControllerActionDescriptor)context.ActionDescriptor).ActionName;
            if (string.Equals(actionName, "Login", StringComparison.OrdinalIgnoreCase))
                return;

            if (await _checkAsync(context.HttpContext)) return;

            var request = context.HttpContext.Request;
            var isApi = request.Headers["X-Requested-With"] == "XMLHttpRequest"
                || (request.Headers["Accept"].FirstOrDefault()?.Contains("application/json") == true);

            if (isApi)
                context.Result = new JsonResult(new { code = 401, message = "Unauthorized" }) { StatusCode = 401 };
            else
                context.Result = new RedirectResult($"/{DashboardController.RoutePrefix}/login");
        }
    }
}
