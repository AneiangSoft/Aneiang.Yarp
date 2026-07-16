using Microsoft.AspNetCore.Http;

namespace Aneiang.Yarp.Infrastructure;

public static class ClientIpResolver
{
    public static string? GetClientIp(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var value = forwardedFor.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                return ExtractFirstIp(value);
        }

        if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp))
        {
            var value = realIp.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                return value.Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }

    public static string? GetConnectionIp(HttpContext context)
    {
        if (context.Connection.RemoteIpAddress != null)
            return context.Connection.RemoteIpAddress.ToString();

        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var value = forwardedFor.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                return StripIpv6PortSuffix(ExtractFirstIp(value));
        }

        return null;
    }

    private static string ExtractFirstIp(string value)
    {
        var commaIdx = value.IndexOf(',');
        return commaIdx > 0
            ? value.AsSpan(0, commaIdx).Trim().ToString()
            : value.Trim();
    }

    private static string StripIpv6PortSuffix(string ip)
    {
        var colonIdx = ip.LastIndexOf(':');
        if (colonIdx > 0 && !ip.AsSpan(0, colonIdx).Contains(':'))
            return ip.Substring(0, colonIdx);
        return ip;
    }
}
