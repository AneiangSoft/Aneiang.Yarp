using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aneiang.Yarp.Dashboard.Controllers;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Extensions
{
    /// <summary>
    /// Aneiang.Yarp.Dashboard service registration extensions.
    /// </summary>
    public static class YarpServiceCollectionExtensions
    {
        /// <summary>
        /// Register Aneiang.Yarp.Dashboard services with default options.
        /// </summary>
        public static IServiceCollection AddAneiangYarpDashboard(this IServiceCollection services)
        {
            return services.AddAneiangYarpDashboard(null);
        }

        /// <summary>
        /// Register Aneiang.Yarp.Dashboard services with custom options.
        /// <para>Options can also be bound from configuration section <c>"Dashboard"</c>.</para>
        /// </summary>
        public static IServiceCollection AddAneiangYarpDashboard(
            this IServiceCollection services,
            Action<DashboardOptions>? configureOptions)
        {
            // Register options from configuration section
            services.AddOptions<DashboardOptions>()
                .BindConfiguration(DashboardOptions.SectionName);

            // Apply user-provided overrides
            if (configureOptions != null)
                services.Configure(configureOptions);

            // Add the controller assembly
            services.AddMvcCore().AddApplicationPart(typeof(DashboardController).Assembly);

            // Register in-memory YARP log capture
            services.AddSingleton<ProxyLogStore>();
            services.AddSingleton<ILoggerProvider, ProxyLogProvider>();

            // Apply route prefix and authorization via conventions
            services.AddSingleton<IConfigureOptions<MvcOptions>>(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<DashboardOptions>>().Value;
                var prefix = options.RoutePrefix.Trim('/');

                // Share config with controller (for login actions)
                DashboardController.RoutePrefix = prefix;
                DashboardController.Options = options;

                // Auto-generate JWT secret if not set
                if (options.JwtSecret == null && options.AuthMode is DashboardAuthMode.CustomJwt or DashboardAuthMode.DefaultJwt)
                {
                    var randomBytes = new byte[32];
                    RandomNumberGenerator.Fill(randomBytes);
                    options.JwtSecret = Convert.ToBase64String(randomBytes);
                }

                return new ConfigureNamedOptions<MvcOptions>(null, mvcOptions =>
                {
                    mvcOptions.Conventions.Add(new DashboardRouteConvention(prefix));

                    // Global auth filter — self-checks action name to skip login actions
                    var authFilter = CreateAuthFilter(options);
                    if (authFilter != null)
                        mvcOptions.Filters.Add(authFilter);
                });
            });

            return services;
        }

        // ═══════════════════════════════════════════════════════
        // Route convention
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Prepends the configured prefix to all DashboardController action routes.
        /// </summary>
        private sealed class DashboardRouteConvention : IApplicationModelConvention
        {
            private readonly string _prefix;

            public DashboardRouteConvention(string prefix) => _prefix = prefix;

            public void Apply(ApplicationModel application)
            {
                var controller = application.Controllers
                    .FirstOrDefault(c => c.ControllerType == typeof(DashboardController));
                if (controller == null) return;

                foreach (var action in controller.Actions)
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

        // ═══════════════════════════════════════════════════════
        // Auth filter factory (supports all 3 modes)
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Creates a <see cref="DashboardAuthFilter"/> based on the configured auth mode.
        /// Returns null if no auth is needed (mode <see cref="DashboardAuthMode.None"/> with no custom delegate).
        /// </summary>
        private static DashboardAuthFilter? CreateAuthFilter(DashboardOptions options)
        {
            if (options.AuthorizeRequest != null)
            {
                // Custom delegate (highest priority)
                var check = options.AuthorizeRequest;
                return new DashboardAuthFilter(ctx => check(ctx));
            }

            if (options.AuthMode == DashboardAuthMode.ApiKey && !string.IsNullOrEmpty(options.ApiKey))
            {
                // Mode 1: API Key
                var apiKey = options.ApiKey;
                var headerName = options.ApiKeyHeaderName;
                return new DashboardAuthFilter(async ctx =>
                {
                    if (ctx.Request.Headers.TryGetValue(headerName, out var hv) && hv.Any(v => v == apiKey))
                        return true;
                    if (ctx.Request.Query.TryGetValue("api-key", out var qv) && qv.Any(v => v == apiKey))
                        return true;
                    return false;
                });
            }

            if (options.AuthMode is DashboardAuthMode.CustomJwt or DashboardAuthMode.DefaultJwt)
            {
                // Mode 2 & 3: JWT
                // Validate token from Authorization header (API calls) or dashboard_token cookie (page loads)
                var secret = options.JwtSecret!;
                return new DashboardAuthFilter(async ctx =>
                {
                    // Check Authorization header (used by authFetch for XHR/API calls)
                    var authHeader = ctx.Request.Headers["Authorization"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                    {
                        var token = authHeader[7..];
                        var (valid, _) = DashboardJwtHelper.ValidateToken(token, secret);
                        if (valid) return true;
                    }

                    // Check dashboard_token cookie (used for browser page navigation)
                    if (ctx.Request.Cookies.TryGetValue("dashboard_token", out var cookieToken)
                        && !string.IsNullOrEmpty(cookieToken))
                    {
                        var (valid, _) = DashboardJwtHelper.ValidateToken(cookieToken, secret);
                        return valid;
                    }

                    return false;
                });
            }

            if (options.AuthMode != DashboardAuthMode.None)
            {
                // Fallback: standard ASP.NET Core policy (only for non-None modes with no other config)
                var policyBuilder = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();
                if (options.ConfigurePolicy != null)
                    options.ConfigurePolicy(policyBuilder);
                else if (options.AllowedRoles?.Length > 0)
                    policyBuilder.RequireRole(options.AllowedRoles);

                var policy = policyBuilder.Build();
                return new DashboardAuthFilter(async ctx =>
                {
                    var authService = ctx.RequestServices.GetRequiredService<IAuthorizationService>();
                    var result = await authService.AuthorizeAsync(ctx.User, policy);
                    return result.Succeeded;
                });
            }

            // AuthMode.None with no custom delegate — no filter
            return null;
        }

        // ═══════════════════════════════════════════════════════
        // Authorization filter
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Lightweight async authorization filter.
        /// Automatically skips login actions (action name "Login").
        /// For browser navigation (non-XHR) it redirects to login page;
        /// for API/XHR requests it returns 401 JSON.
        /// </summary>
        private sealed class DashboardAuthFilter : IAsyncAuthorizationFilter
        {
            private readonly Func<HttpContext, Task<bool>> _checkAsync;

            public DashboardAuthFilter(Func<HttpContext, Task<bool>> checkAsync) => _checkAsync = checkAsync;

            public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
            {
                // Skip login actions — they are public
                var actionDescriptor = context.ActionDescriptor as ControllerActionDescriptor;
                if (string.Equals(actionDescriptor?.ActionName, "Login", StringComparison.OrdinalIgnoreCase))
                    return;

                if (await _checkAsync(context.HttpContext)) return;

                var request = context.HttpContext.Request;
                var isApiRequest = request.Headers["X-Requested-With"] == "XMLHttpRequest"
                    || (request.Headers["Accept"].FirstOrDefault()?.Contains("application/json") == true);

                if (isApiRequest)
                {
                    context.Result = new JsonResult(new { code = 401, message = "Unauthorized" })
                    { StatusCode = 401 };
                }
                else
                {
                    // Browser navigation — redirect to login
                    var loginUrl = $"/{DashboardController.RoutePrefix}/login";
                    context.Result = new RedirectResult(loginUrl);
                }
            }
        }

        // ═══════════════════════════════════════════════════════
        // JWT helper (no external NuGet dependency)
        // ═══════════════════════════════════════════════════════
    }

    /// <summary>JWT token generation and validation (no external NuGet dependency).</summary>
    public static class DashboardJwtHelper
    {
            private static readonly JsonSerializerOptions _jsonOptions = new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

        /// <summary>Generate a signed JWT token.</summary>
        /// <param name="username">Subject claim.</param>
        /// <param name="secret">HMAC-SHA256 signing key.</param>
        public static string GenerateToken(string username, string secret)
            {
                var header = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" }, _jsonOptions);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var payload = JsonSerializer.Serialize(new
                {
                    sub = username,
                    iss = "Aneiang.Yarp.Dashboard",
                    iat = now,
                    exp = now + 28800  // 8 hours
                }, _jsonOptions);

                var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
                var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
                var signingInput = $"{headerB64}.{payloadB64}";

                var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
                var signatureB64 = Base64UrlEncode(signature);

                return $"{headerB64}.{payloadB64}.{signatureB64}";
            }

        /// <summary>Validate a signed JWT token.</summary>
        /// <param name="token">The JWT string.</param>
        /// <param name="secret">HMAC-SHA256 signing key.</param>
        /// <returns>(valid, username) tuple.</returns>
        public static (bool Valid, string? Username) ValidateToken(string token, string secret)
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return (false, null);

                var signingInput = $"{parts[0]}.{parts[1]}";
                var expectedSig = ComputeSignature(signingInput, secret);

                if (!ConstantTimeEquals(Base64UrlDecode(parts[2]), expectedSig))
                    return (false, null);

                var payloadBytes = Base64UrlDecode(parts[1]);
                using var doc = JsonDocument.Parse(payloadBytes);
                var root = doc.RootElement;

                // Check expiry
                if (root.TryGetProperty("exp", out var expEl))
                {
                    var expTime = DateTimeOffset.FromUnixTimeSeconds(expEl.GetInt64());
                    if (expTime < DateTimeOffset.UtcNow) return (false, null);
                }

                var username = root.TryGetProperty("sub", out var subEl) ? subEl.GetString() : null;
                return (true, username);
            }

            private static byte[] ComputeSignature(string input, string secret)
            {
                var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
            }

            private static string Base64UrlEncode(byte[] data)
                => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            private static byte[] Base64UrlDecode(string input)
            {
                var b64 = input.Replace('-', '+').Replace('_', '/');
                var pad = b64.Length % 4;
                if (pad == 2) b64 += "==";
                else if (pad == 3) b64 += "=";
                return Convert.FromBase64String(b64);
            }

            /// <summary>Constant-time comparison to prevent timing attacks.</summary>
            private static bool ConstantTimeEquals(byte[] a, byte[] b)
            {
                if (a.Length != b.Length) return false;
                var diff = 0;
                for (int i = 0; i < a.Length; i++)
                    diff |= a[i] ^ b[i];
                return diff == 0;
            }
        }
}
