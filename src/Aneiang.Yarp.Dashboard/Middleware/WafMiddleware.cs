using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Models;

namespace Aneiang.Yarp.Dashboard.Middleware;

/// <summary>
/// Web Application Firewall middleware.
/// Provides IP blocking, SQL injection detection, XSS detection, path traversal detection, and request size validation.
/// </summary>
public sealed class WafMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WafMiddleware> _logger;
    private readonly WafOptions _options;

    // Compiled regex patterns for built-in rules
    private static readonly Regex SqlInjectionPattern = new(
        @"\b(SELECT|INSERT|UPDATE|DELETE|DROP|UNION|--|\/\*|\*\/)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex XssPattern = new(
        @"<script|javascript:|on\w+\s*=|data:text/html|<iframe|onerror\s*=|onload\s*=",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex PathTraversalPattern = new(
        @"(\.\.\/|\.\.\\|%2e%2e%2f|%2e%2e\/|%2e%2e%5c|%2e%2e\\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex EmailPattern = new(
        @"'|;|--|\/\*|\*\/|xp_|sp_executesql|exec\s*\(|execute\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public WafMiddleware(
        RequestDelegate next,
        ILogger<WafMiddleware> logger,
        IOptions<WafOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        // 1. IP check
        if (_options.EnableIpCheck && !CheckIp(context))
        {
            _logger.LogWarning("WAF blocked request from IP: {Ip}", GetClientIp(context));
            await BlockRequest(context, "Access denied");
            return;
        }

        // 2. Request size check
        if (_options.EnableRequestSizeValidation && !CheckRequestSize(context))
        {
            _logger.LogWarning("WAF blocked oversized request from IP: {Ip}", GetClientIp(context));
            await BlockRequest(context, "Request entity too large");
            return;
        }

        // 3. Header count/size check
        if (_options.EnableRequestSizeValidation && !CheckHeaders(context))
        {
            _logger.LogWarning("WAF blocked malformed headers from IP: {Ip}", GetClientIp(context));
            await BlockRequest(context, "Too many headers");
            return;
        }

        // 4. URI checks
        var uri = context.Request.Path.Value ?? "";
        if (uri.Length > 4096)
        {
            _logger.LogWarning("WAF blocked oversized URI from IP: {Ip}", GetClientIp(context));
            await BlockRequest(context, "URI too long");
            return;
        }

        if (_options.EnablePathTraversalDetection && PathTraversalPattern.IsMatch(uri))
        {
            _logger.LogWarning("WAF blocked path traversal attempt: {Uri} from {Ip}", uri, GetClientIp(context));
            await BlockRequest(context, "Blocked by security policy");
            return;
        }

        // 5. Query string checks
        var queryString = context.Request.QueryString.Value ?? "";
        if (_options.EnableSqlInjectionDetection && EmailPattern.IsMatch(queryString))
        {
            _logger.LogWarning("WAF blocked SQL injection in query from IP: {Ip}", GetClientIp(context));
            await BlockRequest(context, "Blocked by security policy");
            return;
        }

        if (_options.EnablePathTraversalDetection && PathTraversalPattern.IsMatch(queryString))
        {
            _logger.LogWarning("WAF blocked path traversal in query from IP: {Ip}", GetClientIp(context));
            await BlockRequest(context, "Blocked by security policy");
            return;
        }

        await _next(context);
    }

    private bool CheckIp(HttpContext context)
    {
        var clientIp = GetClientIp(context);
        if (string.IsNullOrEmpty(clientIp))
            return true;

        // Check whitelist first (if non-empty, only whitelisted IPs are allowed)
        if (_options.IpWhitelist.Count > 0)
        {
            if (!_options.IpWhitelist.Any(ip => MatchesIp(ip.Trim(), clientIp)))
                return false;
            return true;
        }

        // Check blacklist
        if (_options.IpBlacklist.Count > 0)
        {
            if (_options.IpBlacklist.Any(ip => MatchesIp(ip.Trim(), clientIp)))
                return false;
        }

        return true;
    }

    private static bool MatchesIp(string pattern, string clientIp)
    {
        if (pattern.Contains('/'))
        {
            // CIDR notation (e.g., "192.168.1.0/24")
            return IsInCidrRange(pattern, clientIp);
        }

        if (pattern.Contains('*'))
        {
            // Wildcard (e.g., "192.168.*.*")
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(clientIp, regex, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));
        }

        return string.Equals(pattern, clientIp, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInCidrRange(string cidr, string clientIp)
    {
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2) return false;

            var baseIp = System.Net.IPAddress.Parse(parts[0]);
            var maskBits = int.Parse(parts[1]);
            var clientIpAddr = System.Net.IPAddress.Parse(clientIp);

            var baseBytes = baseIp.GetAddressBytes();
            var clientBytes = clientIpAddr.GetAddressBytes();

            if (baseBytes.Length != clientBytes.Length) return false;

            var fullBytes = maskBits / 8;
            var remainingBits = maskBits % 8;

            for (int i = 0; i < fullBytes; i++)
            {
                if (baseBytes[i] != clientBytes[i]) return false;
            }

            if (remainingBits > 0 && fullBytes < baseBytes.Length)
            {
                var mask = (byte)(0xFF << (8 - remainingBits));
                if ((baseBytes[fullBytes] & mask) != (clientBytes[fullBytes] & mask))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool CheckRequestSize(HttpContext context)
    {
        if (context.Request.ContentLength.HasValue &&
            context.Request.ContentLength.Value > _options.MaxRequestBodySize)
        {
            return false;
        }
        return true;
    }

    private bool CheckHeaders(HttpContext context)
    {
        if (context.Request.Headers.Count > _options.MaxHeaderCount)
            return false;

        foreach (var header in context.Request.Headers)
        {
            if (header.Value.Count > 1 || header.Value.ToString().Length > _options.MaxHeaderSize)
                return false;
        }

        return true;
    }

    private static string? GetClientIp(HttpContext context)
    {
        // Check X-Forwarded-For header first
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var value = forwardedFor.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
            {
                var commaIdx = value.IndexOf(',');
                return commaIdx > 0 ? value.AsSpan(0, commaIdx).Trim().ToString() : value.Trim().ToString();
            }
        }

        // X-Real-IP
        if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp))
        {
            var value = realIp.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                return value.Trim().ToString();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }

    private async Task BlockRequest(HttpContext context, string message)
    {
        context.Response.StatusCode = 403;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Forbidden",
            message = message,
            waf = true
        });
    }
}
