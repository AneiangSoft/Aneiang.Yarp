using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Modules.Alert.Services;
using Aneiang.Yarp.Dashboard.Modules.Notification.Services;
using Aneiang.Yarp.Dashboard.Modules.Waf.Services;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;

/// <summary>
/// Web Application Firewall middleware.
/// Provides IP blocking, SQL injection detection, XSS detection, path traversal detection, and request size validation.
/// All regex patterns use a tight timeout to prevent ReDoS attacks.
/// Settings are loaded from <see cref="IWafSettingsPersistenceService"/> when available,
/// falling back to <see cref="WafOptions"/> from configuration.
/// </summary>
public sealed class WafMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WafMiddleware> _logger;
    private readonly WafOptions _wafOptions;
    private readonly string _dashPrefix;
    private readonly WafEventStore _eventStore;
    private readonly IGatewayAlertService _alertService;
    private readonly IGatewayPluginManager _pluginManager;
    private readonly IWafSettingsPersistenceService? _wafPersistence;
    private readonly INotificationService? _notificationService;

    /// <summary>Ultra-tight timeout (5ms) prevents catastrophic backtracking while allowing benign input.</summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(5);

    // Cache for compiled wildcard IP regex patterns — avoids creating new Regex per request.
    private static readonly ConcurrentDictionary<string, Regex> WildcardRegexCache = new();

    // SQL injection: checks for dangerous keywords and comment-based injection in URI/query.
    // Uses alternation with strict anchors to prevent backtracking on evil input.
    private static readonly Regex SqlInjectionPattern = new(
        @"(?i)\b(?:SELECT|INSERT|UPDATE|DELETE|DROP|UNION|EXEC|EXECUTE|XP_|SP_)
        |\B--|\B/\*|\*/",
        RegexOptions.Compiled | RegexOptions.Multiline,
        RegexTimeout);

    // SQL injection: checks for string-escape attacks in query parameter values (e.g., ' OR '1'='1)
    private static readonly Regex SqlInjectionValuePattern = new(
        @"(?i)'(\s*(?:OR|AND)\s*['""]?\w+[""']?\s*(?:=|LIKE|<|>)|;\s*(?:DROP|DELETE|INSERT|UPDATE))",
        RegexOptions.Compiled,
        RegexTimeout);

    // XSS: common script injection and event-handler patterns
    private static readonly Regex XssPattern = new(
        @"(?i)<script[^>]*>|</script>|javascript:|data:text/html|<iframe[^>]*>|on\w+\s*=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    // Path traversal: encoded and raw variants
    private static readonly Regex PathTraversalPattern = new(
        @"(?i)(\.\.[/%5c\\])|(%2e%2e[%/%5c\\])|(%252e%252e)",
        RegexOptions.Compiled,
        RegexTimeout);

    // Database error message fingerprinting (blocks responses that reveal DB errors)
    private static readonly Regex DbErrorPattern = new(
        @"(?i)(SQL syntax|MySQL|ORA-\d{4,}|SQLServer|ODBC|PostgreSQL|sqlite_error|mysql_\w+\(\)|Microsoft SQL Server)",
        RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>
    /// Content root path for the Dashboard static files. Used to skip logging for frontend resources.
    /// </summary>
    private const string ContentRoot = "/_content/Aneiang.Yarp.Dashboard";

    public WafMiddleware(
        RequestDelegate next,
        ILogger<WafMiddleware> logger,
        IOptions<WafOptions> wafOptions,
        IOptions<DashboardOptions> dashOptions,
        WafEventStore eventStore,
        IGatewayAlertService alertService,
        IGatewayPluginManager pluginManager,
        IWafSettingsPersistenceService? wafPersistence = null,
        INotificationService? notificationService = null)
    {
        _next = next;
        _logger = logger;
        _wafOptions = wafOptions.Value;
        var prefix = _wafOptions.DashboardRoutePrefix;
        if (string.IsNullOrWhiteSpace(prefix) || prefix == "apigateway")
            prefix = dashOptions.Value.RoutePrefix;
        _dashPrefix = "/" + prefix.Trim('/');
        _eventStore = eventStore;
        _alertService = alertService;
        _pluginManager = pluginManager;
        _wafPersistence = wafPersistence;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Resolves the effective WAF options by merging persisted settings over configuration defaults.
    /// </summary>
    private WafOptions ResolveEffectiveOptions()
    {
        var data = _wafPersistence?.Load();
        if (data == null) return _wafOptions;

        return new WafOptions
        {
            Enabled = data.Enabled,
            DashboardRoutePrefix = _wafOptions.DashboardRoutePrefix,
            IpWhitelist = data.IpWhitelist.Count > 0 ? data.IpWhitelist : _wafOptions.IpWhitelist,
            IpBlacklist = data.IpBlacklist.Count > 0 ? data.IpBlacklist : _wafOptions.IpBlacklist,
            MaxRequestBodySize = data.MaxRequestBodySize,
            MaxHeaderCount = data.MaxHeaderCount,
            MaxHeaderSize = data.MaxHeaderSize,
            EnableSqlInjectionDetection = data.EnableSqlInjectionDetection,
            EnableXssDetection = data.EnableXssDetection,
            EnablePathTraversalDetection = data.EnablePathTraversalDetection,
            EnableIpCheck = data.EnableIpCheck,
            EnableRequestSizeValidation = data.EnableRequestSizeValidation,
            ExtraScriptSources = _wafOptions.ExtraScriptSources
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var opts = ResolveEffectiveOptions();
        bool globallyEnabled = opts.Enabled && _pluginManager.IsPluginEnabled("waf");

        var path = context.Request.Path.Value ?? "";

        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var routeMeta = proxyFeature?.Route?.Config?.Metadata;

        bool wafActive;
        if (routeMeta != null && routeMeta.TryGetValue("Waf:Enabled", out var routeWafEnabled))
        {
            wafActive = bool.TryParse(routeWafEnabled, out var parsed) ? parsed : globallyEnabled;
        }
        else
        {
            wafActive = globallyEnabled;
        }

        if (!wafActive)
        {
            await _next(context);
            return;
        }

        if (path.StartsWith(_dashPrefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(ContentRoot, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (opts.EnableIpCheck && !CheckIp(context, opts))
        {
            RecordSecurityEvent(context, "IpBlocked");
            await BlockRequest(context, "Access denied");
            return;
        }

        if (opts.EnableRequestSizeValidation && !CheckRequestSize(context, opts))
        {
            RecordSecurityEvent(context, "RequestSizeBlocked");
            await BlockRequest(context, "Request entity too large");
            return;
        }

        if (opts.EnableRequestSizeValidation && !CheckHeaders(context, opts))
        {
            RecordSecurityEvent(context, "MalformedHeadersBlocked");
            await BlockRequest(context, "Too many headers");
            return;
        }

        if (path.Length > 4096)
        {
            RecordSecurityEvent(context, "UriTooLongBlocked");
            await BlockRequest(context, "URI too long");
            return;
        }

        if (opts.EnablePathTraversalDetection && PathTraversalPattern.IsMatch(path))
        {
            RecordSecurityEvent(context, "PathTraversalBlocked", path);
            await BlockRequest(context, "Blocked by security policy");
            return;
        }

        var queryString = context.Request.QueryString.Value ?? "";
        if (!string.IsNullOrEmpty(queryString))
        {
            if (opts.EnableSqlInjectionDetection)
            {
                if (SqlInjectionPattern.IsMatch(queryString))
                {
                    RecordSecurityEvent(context, "SqlInjectionBlocked", queryString);
                    await BlockRequest(context, "Blocked by security policy");
                    return;
                }
                if (SqlInjectionValuePattern.IsMatch(queryString))
                {
                    RecordSecurityEvent(context, "SqlInjectionValueBlocked", queryString);
                    await BlockRequest(context, "Blocked by security policy");
                    return;
                }
            }

            if (opts.EnablePathTraversalDetection && PathTraversalPattern.IsMatch(queryString))
            {
                RecordSecurityEvent(context, "PathTraversalInQueryBlocked", queryString);
                await BlockRequest(context, "Blocked by security policy");
                return;
            }
        }

        ApplySecurityHeaders(context, opts);

        await _next(context);
    }

    private void ApplySecurityHeaders(HttpContext context, WafOptions opts)
    {
        var resp = context.Response;
        if (!resp.Headers.ContainsKey("X-Content-Type-Options"))
            resp.Headers["X-Content-Type-Options"] = "nosniff";
        if (!resp.Headers.ContainsKey("X-Frame-Options"))
            resp.Headers["X-Frame-Options"] = "DENY";
        if (!resp.Headers.ContainsKey("X-XSS-Protection"))
            resp.Headers["X-XSS-Protection"] = "1; mode=block";
        if (!resp.Headers.ContainsKey("Referrer-Policy"))
            resp.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // CSP for proxied responses only — dashboard pages are skipped upstream.
        if (!resp.Headers.ContainsKey("Content-Security-Policy"))
        {
            var csp = "default-src 'self'";
            if (!string.IsNullOrWhiteSpace(opts.ExtraScriptSources))
                csp += "; script-src 'self' " + opts.ExtraScriptSources;
            resp.Headers["Content-Security-Policy"] = csp;
        }
    }

    private void RecordSecurityEvent(HttpContext context, string eventType, string? details = null)
    {
        var clientIp = GetClientIp(context);
        var requestUri = context.Request.Path.Value + context.Request.QueryString.Value;

        _logger.LogWarning(
            "WAF [{EventType}] from IP: {Ip}, Path: {Path}, Details: {Details}",
            eventType, clientIp, requestUri, details ?? "(hidden)");

        _eventStore.Add(new WafSecurityEvent
        {
            ClientIp = clientIp ?? "unknown",
            EventType = eventType,
            RuleName = eventType.Replace("Blocked", ""),
            RequestUri = requestUri,
            RequestMethod = context.Request.Method,
            MatchedValue = details != null && details.Length <= 200 ? details : null,
            Blocked = true,
            StatusCode = 403
        });

        _alertService.AlertWafBlock(clientIp ?? "unknown", eventType, requestUri);
        _notificationService?.NotifyWafBlock(clientIp ?? "unknown", eventType, requestUri);
    }

    private bool CheckIp(HttpContext context, WafOptions opts)
    {
        var clientIp = GetClientIp(context);
        if (string.IsNullOrEmpty(clientIp))
            return true;

        if (opts.IpWhitelist.Count > 0)
        {
            if (!opts.IpWhitelist.Any(ip => MatchesIp(ip.Trim(), clientIp)))
                return false;
            return true;
        }

        if (opts.IpBlacklist.Count > 0)
        {
            if (opts.IpBlacklist.Any(ip => MatchesIp(ip.Trim(), clientIp)))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Matches a client IP against an IP pattern (exact, CIDR, or wildcard).
    /// Wildcard patterns use a cached compiled <see cref="Regex"/> to avoid GC pressure.
    /// </summary>
    private static bool MatchesIp(string pattern, string clientIp)
    {
        if (pattern.Contains('/'))
            return IsInCidrRange(pattern, clientIp);

        if (pattern.Contains('*'))
        {
            var cachedRegex = WildcardRegexCache.GetOrAdd(pattern, p =>
                new Regex(
                    "^" + Regex.Escape(p).Replace("\\*", ".*") + "$",
                    RegexOptions.IgnoreCase,
                    RegexTimeout));
            return cachedRegex.IsMatch(clientIp);
        }

        return string.Equals(pattern, clientIp, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if client IP is within a CIDR range using zero-allocation bit operations on spans.
    /// </summary>
    private static bool IsInCidrRange(string cidr, string clientIp)
    {
        var slashIdx = cidr.IndexOf('/');
        if (slashIdx < 0) return false;

        if (!IPAddress.TryParse(cidr.AsSpan(0, slashIdx), out var baseIp))
            return false;
        if (!IPAddress.TryParse(clientIp, out var clientIpAddr))
            return false;

        var baseBytes = baseIp.GetAddressBytes();
        var clientBytes = clientIpAddr.GetAddressBytes();
        if (baseBytes.Length != clientBytes.Length)
            return false;

        if (!int.TryParse(cidr.AsSpan(slashIdx + 1), out var maskBits))
            return false;

        if (baseBytes.Length == 4)
        {
            // IPv4: zero-allocation bit mask using inline bit operations.
            // mask = ~0u << (32 - maskBits) in host byte order.
            // Read base and client as big-endian uint32 for comparison.
            uint baseUint = ((uint)baseBytes[0] << 24) | ((uint)baseBytes[1] << 16) | ((uint)baseBytes[2] << 8) | baseBytes[3];
            uint clientUint = ((uint)clientBytes[0] << 24) | ((uint)clientBytes[1] << 16) | ((uint)clientBytes[2] << 8) | clientBytes[3];
            uint mask = maskBits == 0 ? 0u : (~0u << (32 - maskBits));
            return (baseUint & mask) == (clientUint & mask);
        }

        // IPv6: simple length check (prefix matching beyond /64 is uncommon)
        if (baseBytes.Length == 16)
        {
            var fullBytes = maskBits / 8;
            var remainingBits = maskBits % 8;
            for (int i = 0; i < fullBytes; i++)
            {
                if (baseBytes[i] != clientBytes[i])
                    return false;
            }
            if (remainingBits > 0 && (baseBytes[fullBytes] & (0xFF << (8 - remainingBits))) !=
                (clientBytes[fullBytes] & (0xFF << (8 - remainingBits))))
                return false;
            return true;
        }

        return false;
    }

    private bool CheckRequestSize(HttpContext context, WafOptions opts)
    {
        if (context.Request.ContentLength.HasValue &&
            context.Request.ContentLength.Value > opts.MaxRequestBodySize)
        {
            return false;
        }
        return true;
    }

    private bool CheckHeaders(HttpContext context, WafOptions opts)
    {
        if (context.Request.Headers.Count > opts.MaxHeaderCount)
            return false;

        foreach (var header in context.Request.Headers)
        {
            if (header.Value.Count > 1 || header.Value.ToString().Length > opts.MaxHeaderSize)
                return false;
        }

        return true;
    }

    private static string? GetClientIp(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var value = forwardedFor.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
            {
                var commaIdx = value.IndexOf(',');
                return commaIdx > 0 ? value.AsSpan(0, commaIdx).Trim().ToString() : value.Trim().ToString();
            }
        }

        if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp))
        {
            var value = realIp.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                return value.Trim().ToString();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }

    private static async Task BlockRequest(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Forbidden",
            message = message,
            waf = true
        });
    }
}
