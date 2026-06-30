using Microsoft.AspNetCore.Http;

namespace Aneiang.Yarp.Infrastructure;

/// <summary>
/// Unified client IP resolution from HTTP headers.
/// Centralizes the X-Forwarded-For / X-Real-IP parsing that was previously duplicated across
/// WAF middleware, rate-limit middleware, controllers, and load-balancing policies.
/// </summary>
public static class ClientIpResolver
{
    /// <summary>
    /// Resolves the originating client IP using the standard proxy-forwarding chain:
    /// X-Forwarded-For → X-Real-IP → connection remote address.
    /// This is the recommended method for audit, WAF, and rate-limiting scenarios
    /// where the client identity (behind a trusted proxy) matters.
    /// </summary>
    public static string? GetClientIp(HttpContext context)
    {
        // X-Forwarded-For: comma-separated list, first entry is the originating client
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var value = forwardedFor.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                return ExtractFirstIp(value);
        }

        // X-Real-IP: single IP set by some reverse proxies (e.g. nginx proxy_protocol)
        if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp))
        {
            var value = realIp.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                return value.Trim();
        }

        // Fallback to direct connection (gateway's own peer, or proxyless deployment)
        return context.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Resolves the connection-facing IP, preferring the direct connection endpoint.
    /// This is intended for load-balancing scenarios (e.g. sticky sessions) where the
    /// actual network peer is more relevant than a potentially spoofed forwarded header.
    /// </summary>
    public static string? GetConnectionIp(HttpContext context)
    {
        if (context.Connection.RemoteIpAddress != null)
            return context.Connection.RemoteIpAddress.ToString();

        // Fallback to X-Forwarded-For for proxied deployments
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var value = forwardedFor.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                return StripIpv6PortSuffix(ExtractFirstIp(value));
        }

        return null;
    }

    /// <summary>
    /// Extracts the first IP from a comma-separated X-Forwarded-For value.
    /// Uses <see cref="ReadOnlySpan{T}"/> to avoid string allocations.
    /// </summary>
    private static string ExtractFirstIp(string value)
    {
        var commaIdx = value.IndexOf(',');
        return commaIdx > 0
            ? value.AsSpan(0, commaIdx).Trim().ToString()
            : value.Trim();
    }

    /// <summary>
    /// Strips an IPv4-mapped IPv6 port suffix (e.g. ::ffff:192.168.1.1:8080 → ::ffff:192.168.1.1).
    /// Only removes the suffix when there is exactly one colon (port separator), preserving
    /// genuine IPv6 addresses with multiple colon groups.
    /// </summary>
    private static string StripIpv6PortSuffix(string ip)
    {
        var colonIdx = ip.LastIndexOf(':');
        if (colonIdx > 0 && !ip.AsSpan(0, colonIdx).Contains(':'))
            return ip.Substring(0, colonIdx);
        return ip;
    }
}
