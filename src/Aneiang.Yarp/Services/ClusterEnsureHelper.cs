using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

internal static class ClusterEnsureHelper
{
    public static string GetDestinationKey(string clientIp) =>
        $"ip-{clientIp.Replace(".", "-")}";

    #region IP-isolation cluster

    public static List<ClusterConfig> EnsureIpCluster(
        List<ClusterConfig> clusters,
        string clusterName,
        string clientIp,
        string destinationAddress)
    {
        var destKey = GetDestinationKey(clientIp);
        var existingIdx = clusters.FindIndex(c =>
            string.Equals(c.ClusterId, clusterName, StringComparison.OrdinalIgnoreCase));

        if (existingIdx >= 0)
        {
            var existing = clusters[existingIdx];
            var destinations = existing.Destinations?.ToDictionary(
                d => d.Key, d => d.Value) ?? new Dictionary<string, DestinationConfig>();

            destinations[destKey] = new DestinationConfig
            {
                Address = destinationAddress,
                Metadata = new Dictionary<string, string> { { "ClientIp", clientIp } }
            };

            clusters[existingIdx] = new ClusterConfig
            {
                ClusterId = clusterName,
                Destinations = destinations,
                LoadBalancingPolicy = existing.LoadBalancingPolicy ?? "IpBased",
                HealthCheck = existing.HealthCheck
            };
        }
        else
        {
            clusters.Add(new ClusterConfig
            {
                ClusterId = clusterName,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    [destKey] = new DestinationConfig
                    {
                        Address = destinationAddress,
                        Metadata = new Dictionary<string, string> { { "ClientIp", clientIp } }
                    }
                },
                LoadBalancingPolicy = "IpBased",
                HealthCheck = DynamicYarpConfigHelpers.BuildClusterHealthCheck(null)
            });
        }

        return clusters;
    }

    #endregion

    #region Normal (non-IP) cluster

    public static List<ClusterConfig> EnsureNormalCluster(
        List<ClusterConfig> clusters,
        string clusterName,
        string destinationAddress)
    {
        var existingIdx = clusters.FindIndex(c =>
            string.Equals(c.ClusterId, clusterName, StringComparison.OrdinalIgnoreCase));

        if (existingIdx >= 0)
        {
            if (!string.IsNullOrWhiteSpace(destinationAddress))
            {
                var existing = clusters[existingIdx];
                clusters[existingIdx] = new ClusterConfig
                {
                    ClusterId = clusterName,
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        ["d1"] = new() { Address = destinationAddress }
                    },
                    HealthCheck = existing.HealthCheck
                };
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(destinationAddress))
            {
                clusters.Add(new ClusterConfig
                {
                    ClusterId = clusterName,
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        ["d1"] = new() { Address = destinationAddress }
                    }
                });
            }
        }

        return clusters;
    }

    #endregion

    #region IP destination removal

    public static (List<ClusterConfig> Clusters, string DestKey, bool ClusterRemoved, bool Found)
        RemoveIpDestination(
            List<ClusterConfig> clusters,
            string clusterId,
            string clientIp)
    {
        var destKey = GetDestinationKey(clientIp);
        var cluster = clusters.FirstOrDefault(c =>
            string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

        if (cluster == null)
            return (clusters, destKey, false, false);

        var destinations = cluster.Destinations?.ToDictionary(
            d => d.Key, d => d.Value) ?? new Dictionary<string, DestinationConfig>();

        if (!destinations.Remove(destKey))
            return (clusters, destKey, false, false); // destination not present

        var mutable = new List<ClusterConfig>(clusters);
        var idx = mutable.FindIndex(c =>
            string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

        if (idx < 0)
            return (clusters, destKey, false, true);

        if (destinations.Count == 0)
        {
            mutable.RemoveAt(idx);
            return (mutable, destKey, true, true);
        }

        mutable[idx] = new ClusterConfig
        {
            ClusterId = cluster.ClusterId,
            Destinations = destinations,
            LoadBalancingPolicy = cluster.LoadBalancingPolicy,
            Metadata = cluster.Metadata
        };

        return (mutable, destKey, false, true);
    }

    #endregion
}
