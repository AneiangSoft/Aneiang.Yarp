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
                var ipFromKey = dest.DestinationId.Substring(3).Replace("-", ".");
                if (string.Equals(ipFromKey, clientIp, StringComparison.OrdinalIgnoreCase))
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
        // Check X-Forwarded-For header first (if behind proxy)
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var ip = forwardedFor.FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(ip))
            {
                var colonIndex = ip.IndexOf(':');
                if (colonIndex > 0) ip = ip.Substring(0, colonIndex);
                return ip;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}
