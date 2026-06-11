namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

/// <summary>Request body for creating a manual configuration snapshot.</summary>
public class SnapshotRequest
{
    /// <summary>Optional description for the snapshot.</summary>
    public string? Description { get; set; }
}
