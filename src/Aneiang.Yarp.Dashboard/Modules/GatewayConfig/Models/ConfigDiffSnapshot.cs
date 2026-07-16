using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

/// <summary>Represents a snapshot of the gateway configuration at a specific point in time.
/// Used for config diff and comparison.</summary>
public class ConfigDiffSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = "system";
    public string? Source { get; set; }
    public string? Description { get; set; }

    /// <summary>Snapshot of all routes.</summary>
    public List<RouteSnapshot> Routes { get; set; } = new();

    /// <summary>Snapshot of all clusters.</summary>
    public List<ClusterSnapshot> Clusters { get; set; } = new();
}

