using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Infrastructure.State;

/// <summary>
/// Helper methods for circuit key generation and parsing.
/// </summary>
public static class CircuitKeyHelper
{
    /// <summary>
    /// Build a stable circuit key from cluster and destination identifiers.
    /// </summary>
    public static string BuildCircuitKey(string clusterId, string? clusterUid, string? destinationKey)
    {
        var resolvedClusterUid = string.IsNullOrEmpty(clusterUid) && !string.IsNullOrEmpty(clusterId)
            ? StableUid.FromKey("cluster", clusterId) : clusterUid;
        return $"{resolvedClusterUid}:{ResolveDestinationUid(destinationKey)}";
    }

    /// <summary>
    /// Resolve a destination UID from a destination key.
    /// </summary>
    public static string ResolveDestinationUid(string? destinationKey)
        => string.IsNullOrWhiteSpace(destinationKey) ? "any" : StableUid.FromKey("destination", destinationKey);

    /// <summary>
    /// Parse a circuit key back into cluster ID and destination ID components.
    /// </summary>
    public static (string ClusterId, string? DestinationId) ParseCircuitKey(string key)
    {
        var lastColon = key.LastIndexOf(':');
        if (lastColon < 0) return (key, null);
        var cluster = key[..lastColon];
        var dest = key[(lastColon + 1)..];
        return dest == "any" ? (cluster, null) : (cluster, dest);
    }
}
