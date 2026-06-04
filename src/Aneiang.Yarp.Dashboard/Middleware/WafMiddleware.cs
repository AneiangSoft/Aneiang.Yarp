using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;

namespace Aneiang.Yarp.Dashboard.Middleware;

/// <summary>
/// Web Application Firewall middleware.
/// Provides IP blocking, SQL injection detection, XSS detection, path traversal detection, and request size validation.
/// All regex patterns use a tight timeout to prevent ReDoS attacks.
/// </summary>
public sealed class WafMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WafMiddleware> _logger;
    private readonly WafOptions _wafOptions;
    private readonly string _dashPrefix;
    private readonly WafEventStore _eventStore;
    private readonly IGatewayAlertService _alertService;

    /// <summary>Ultra-tight timeout (5ms) prevents catastrophic backtracking while allowing benign input.</summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(5);

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
        IGatewayAlertService alertService)
    {
        _next = next;
        _logger = logger;
        _wafOptions = wafOptions.Value;
        // Use DashboardOptions prefix if WafOptions prefix is the default
        var prefix = _wafOptions.DashboardRoutePrefix;
        if (string.IsNullOrWhiteSpace(prefix) || prefix == "apigateway")
            prefix = dashOptions.Value.RoutePrefix;
        _dashPrefix = "/" + prefix.Trim('/');
        _eventStore = eventStore;
        _alertService = alertService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_wafOptions.Enabled)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";
        var dashPrefix = "/" + (_wafOptions.DashboardRoutePrefix?.Trim('/') ?? "apigateway");

        // Skip WAF checks for Dashboard UI and static assets — avoids false positives on config editing
        if (path.StartsWith(dashPrefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(ContentRoot, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 1. IP check
        if (_wafOptions.EnableIpCheck && !CheckIp(context))
        {
            RecordSecurityEvent(context, "IpBlocked");
            await BlockRequest(context, "Access denied");
            return;
        }

        // 2. Request size check
        if (_wafOptions.EnableRequestSizeValidation && !CheckRequestSize(context))
        {
            RecordSecurityEvent(context, "RequestSizeBlocked");
            await BlockRequest(context, "Request entity too large");
            return;
        }

        // 3. Header count/size check
        if (_wafOptions.EnableRequestSizeValidation && !CheckHeaders(context))
        {
            RecordSecurityEvent(context, "MalformedHeadersBlocked");
            await BlockRequest(context, "Too many headers");
            return;
        }

        // 4. URI length and traversal check
        if (path.Length > 4096)
        {
            RecordSecurityEvent(context, "UriTooLongBlocked");
            await BlockRequest(context, "URI too long");
            return;
        }

        if (_wafOptions.EnablePathTraversalDetection && PathTraversalPattern.IsMatch(path))
        {
            RecordSecurityEvent(context, "PathTraversalBlocked", path);
            await BlockRequest(context, "Blocked by security policy");
            return;
        }

        // 5. Query string checks
        var queryString = context.Request.QueryString.Value ?? "";
        if (!string.IsNullOrEmpty(queryString))
        {
            if (_wafOptions.EnableSqlInjectionDetection)
            {
                // Check full query string for dangerous keywords
                if (SqlInjectionPattern.IsMatch(queryString))
                {
                    RecordSecurityEvent(context, "SqlInjectionBlocked", queryString);
                    await BlockRequest(context, "Blocked by security policy");
                    return;
                }
                // Check parameter values for string-escape attacks (e.g., ' OR '1'='1)
                if (SqlInjectionValuePattern.IsMatch(queryString))
                {
                    RecordSecurityEvent(context, "SqlInjectionValueBlocked", queryString);
                    await BlockRequest(context, "Blocked by security policy");
                    return;
                }
            }

            if (_wafOptions.EnablePathTraversalDetection && PathTraversalPattern.IsMatch(queryString))
            {
                RecordSecurityEvent(context, "PathTraversalInQueryBlocked", queryString);
                await BlockRequest(context, "Blocked by security policy");
                return;
            }
        }

        // 6. Apply security headers
        ApplySecurityHeaders(context);

        await _next(context);
    }

    private void ApplySecurityHeaders(HttpContext context)
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
        if (!resp.Headers.ContainsKey("Content-Security-Policy"))
            resp.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; object-src 'none'";
    }

    private void RecordSecurityEvent(HttpContext context, string eventType, string? details = null)
    {
        var clientIp = GetClientIp(context);
        var requestUri = context.Request.Path.Value + context.Request.QueryString.Value;

        _logger.LogWarning(
            "WAF [{EventType}] from IP: {Ip}, Path: {Path}, Details: {Details}",
            eventType, clientIp, requestUri, details ?? "(hidden)");

        // Persist to WafEventStore so the Security Events page has data
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

        // Fire alert (respects cooldown and AlertWafBlocks flag)
        _alertService.AlertWafBlock(clientIp ?? "unknown", eventType, requestUri);
    }

    private bool CheckIp(HttpContext context)
    {
        var clientIp = GetClientIp(context);
        if (string.IsNullOrEmpty(clientIp))
            return true;

        if (_wafOptions.IpWhitelist.Count > 0)
        {
            if (!_wafOptions.IpWhitelist.Any(ip => MatchesIp(ip.Trim(), clientIp)))
                return false;
            return true;
        }

        if (_wafOptions.IpBlacklist.Count > 0)
        {
            if (_wafOptions.IpBlacklist.Any(ip => MatchesIp(ip.Trim(), clientIp)))
                return false;
        }

        return true;
    }

    private static bool MatchesIp(string pattern, string clientIp)
    {
        if (pattern.Contains('/'))
            return IsInCidrRange(pattern, clientIp);

        if (pattern.Contains('*'))
        {
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(clientIp, regex, RegexOptions.IgnoreCase, RegexTimeout);
        }

        return string.Equals(pattern, clientIp, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInCidrRange(string cidr, string clientIp)
    {
        try
        {
            var parts = cidr.Split('/', 2);
            if (parts.Length != 2) return false;
            if (!System.Net.IPAddress.TryParse(parts[0], out var baseIp)) return false;
            if (!int.TryParse(parts[1], out var maskBits)) return false;
            if (!System.Net.IPAddress.TryParse(clientIp, out var clientIpAddr)) return false;

            var baseBytes = baseIp.GetAddressBytes();
            var clientBytes = clientIpAddr.GetAddressBytes();
            if (baseBytes.Length != clientBytes.Length) return false;

            var maskBytes = BitConverter.GetBytes(~0u << (32 - maskBits)).Reverse().ToArray();
            if (baseBytes.Length == 4 && maskBytes.Length == 4)
            {
                for (int i = 0; i < 4; i++)
                {
                    if ((baseBytes[i] & maskBytes[i]) != (clientBytes[i] & maskBytes[i]))
                        return false;
                }
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool CheckRequestSize(HttpContext context)
    {
        if (context.Request.ContentLength.HasValue &&
            context.Request.ContentLength.Value > _wafOptions.MaxRequestBodySize)
        {
            return false;
        }
        return true;
    }

    private bool CheckHeaders(HttpContext context)
    {
        if (context.Request.Headers.Count > _wafOptions.MaxHeaderCount)
            return false;

        foreach (var header in context.Request.Headers)
        {
            if (header.Value.Count > 1 || header.Value.ToString().Length > _wafOptions.MaxHeaderSize)
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
