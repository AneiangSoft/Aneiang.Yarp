using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Custom YARP load balancing policy that routes requests based on client IP address.
/// Selects the destination tagged with the matching client IP in its metadata.
/// Falls back to the first available destination if no IP match is found.
/// </summary>
public sealed class IpBasedLoadBalancingPolicy : ILoadBalancingPolicy
{
    /// <inheritdoc />
    public string Name => "IpBased";

    /// <inheritdoc />
    public DestinationState? PickDestination(
        HttpContext context,
        ClusterState cluster,
        IReadOnlyList<DestinationState> availableDestinations)
    {
        if (availableDestinations.Count == 0) return null;
        if (availableDestinations.Count == 1) return availableDestinations[0];

        var clientIp = GetClientIpAddress(context);
        if (clientIp == null) return availableDestinations[0];

        foreach (var dest in availableDestinations)
        {
            // Check metadata first (in-memory, from registration)
            if (dest.Model?.Config?.Metadata != null &&
                dest.Model.Config.Metadata.TryGetValue("ClientIp", out var ip) &&
                string.Equals(ip, clientIp, StringComparison.OrdinalIgnoreCase))
            {
                return dest;
            }

            // Fallback: check destination key for persistence-reload (ip-{address})
            if (dest.DestinationId.StartsWith("ip-", StringComparison.OrdinalIgnoreCase))
            {
                // Avoid string.Replace allocation: parse key directly
                // Key format: ip-192-168-1-100 → 192.168.1.100
                var keySpan = dest.DestinationId.AsSpan(3);
                var ipFromKey = RestoreIpAddress(keySpan);
                if (MemoryExtensions.Equals(ipFromKey, clientIp.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return dest;
                }
            }
        }

        // No IP match, return first available destination
        return availableDestinations[0];
    }

    private static string? GetClientIpAddress(HttpContext context)
    {
        // Prefer direct connection IP (most reliable, no parsing needed)
        if (context.Connection.RemoteIpAddress != null)
            return context.Connection.RemoteIpAddress.ToString();

        // Fallback: check X-Forwarded-For header (if behind proxy)
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var value = forwardedFor.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
            {
                // Avoid Split() allocation: find first comma and trim
                var commaIdx = value.IndexOf(',');
                var ipSegment = commaIdx > 0 ? value.AsSpan(0, commaIdx).Trim() : value.AsSpan().Trim();

                // Strip IPv6 port suffix (e.g. ::ffff:192.168.1.1:8080 → ::ffff:192.168.1.1)
                var colonIdx = ipSegment.LastIndexOf(':');
                if (colonIdx > 0 && !ipSegment.Slice(0, colonIdx).Contains(':'))
                {
                    // Only one colon → it's a port separator, not IPv6
                    ipSegment = ipSegment.Slice(0, colonIdx);
                }

                return ipSegment.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// Convert "192-168-1-100" back to "192.168.1.100".
    /// </summary>
    private static string RestoreIpAddress(ReadOnlySpan<char> keySpan)
    {
        if (keySpan.IsEmpty)
            return string.Empty;

        return string.Create(keySpan.Length, keySpan.ToString(), static (span, src) =>
        {
            for (int i = 0; i < src.Length; i++)
                span[i] = src[i] == '-' ? '.' : src[i];
        });
    }
}
