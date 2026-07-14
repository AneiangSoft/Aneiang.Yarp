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

/// <summary>Route data captured in a config snapshot.</summary>
public class RouteSnapshot
{
    public string RouteId { get; set; } = string.Empty;
    public string ClusterId { get; set; } = string.Empty;
    public string? MatchPath { get; set; }
    public int Order { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>Cluster data captured in a config snapshot.</summary>
public class ClusterSnapshot
{
    public string ClusterId { get; set; } = string.Empty;
    public List<DestinationSnapshot> Destinations { get; set; } = new();
    public string? LoadBalancing { get; set; }
}

/// <summary>Destination data captured in a cluster snapshot.</summary>
public class DestinationSnapshot
{
    public string DestinationId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

/// <summary>Result of comparing two config snapshots.</summary>
public class ConfigDiffResult
{
    public string FromVersion { get; set; } = string.Empty;
    public string ToVersion { get; set; } = string.Empty;
    public DateTime ComparedAt { get; set; } = DateTime.Now;
    public DiffSummary Summary { get; set; } = new();
    public List<DiffEntry> Changes { get; set; } = new();
}

/// <summary>Summary of changes in a diff.</summary>
public class DiffSummary
{
    public int RoutesAdded { get; set; }
    public int RoutesRemoved { get; set; }
    public int RoutesModified { get; set; }
    public int ClustersAdded { get; set; }
    public int ClustersRemoved { get; set; }
    public int ClustersModified { get; set; }
    public int DestinationsAdded { get; set; }
    public int DestinationsRemoved { get; set; }
    public int DestinationsModified { get; set; }
    public int TotalChanges => RoutesAdded + RoutesRemoved + RoutesModified
                             + ClustersAdded + ClustersRemoved + ClustersModified
                             + DestinationsAdded + DestinationsRemoved + DestinationsModified;
}

/// <summary>Individual change entry in a diff.</summary>
public class DiffEntry
{
    public DiffChangeType ChangeType { get; set; }
    public DiffEntityType EntityType { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string? ParentId { get; set; }

    /// <summary>All field-level changes for this entity. Replaces the single FieldChange property.</summary>
    public List<DiffFieldChange> FieldChanges { get; set; } = new();

    /// <summary>Human-readable description of the change.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>Type of change.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiffChangeType
{
    Added,
    Removed,
    Modified
}

/// <summary>Type of entity that changed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiffEntityType
{
    Route,
    Cluster,
    Destination
}

/// <summary>Details of a field-level change.</summary>
public class DiffFieldChange
{
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
