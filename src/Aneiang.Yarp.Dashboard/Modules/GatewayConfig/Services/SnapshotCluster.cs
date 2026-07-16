namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>Snapshot cluster entry extracted from snapshot config.</summary>
public class SnapshotCluster
{
    public string ClusterId { get; set; } = string.Empty;
    public string? LoadBalancingPolicy { get; set; }
    public Dictionary<string, string> Destinations { get; set; } = new();
}
