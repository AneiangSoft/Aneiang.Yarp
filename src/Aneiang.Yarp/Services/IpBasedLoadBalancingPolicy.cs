using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Model;
using Aneiang.Yarp.Infrastructure;

namespace Aneiang.Yarp.Services;

public sealed class IpBasedLoadBalancingPolicy : ILoadBalancingPolicy
{
    public string Name => "IpBased";

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
        return ClientIpResolver.GetConnectionIp(context);
    }

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
