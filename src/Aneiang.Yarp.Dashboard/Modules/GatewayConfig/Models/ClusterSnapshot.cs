namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

/// <summary>Cluster data captured in a config snapshot.</summary>
public class ClusterSnapshot
{
    public string ClusterId { get; set; } = string.Empty;
    public List<DestinationSnapshot> Destinations { get; set; } = new();
    public string? LoadBalancing { get; set; }
}
