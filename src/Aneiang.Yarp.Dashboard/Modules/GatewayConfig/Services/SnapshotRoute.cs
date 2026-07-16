namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>Snapshot route entry extracted from snapshot config.</summary>
public class SnapshotRoute
{
    public string RouteId { get; set; } = string.Empty;
    public string ClusterId { get; set; } = string.Empty;
    public string MatchPath { get; set; } = string.Empty;
    public int Order { get; set; }
}
