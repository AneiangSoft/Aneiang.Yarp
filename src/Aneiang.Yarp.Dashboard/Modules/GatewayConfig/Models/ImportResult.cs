namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

/// <summary>Detailed result of a configuration import operation.</summary>
public class ImportResult
{
    /// <summary>Whether the import completed (true even if some items were skipped).</summary>
    public bool Success { get; set; }

    /// <summary>Number of route entries found in the uploaded JSON.</summary>
    public int TotalRoutes { get; set; }

    /// <summary>Routes successfully added/updated.</summary>
    public int ImportedRoutes { get; set; }

    /// <summary>Routes skipped (missing clusterId, etc.).</summary>
    public int SkippedRoutes { get; set; }

    /// <summary>Number of cluster entries found in the uploaded JSON.</summary>
    public int TotalClusters { get; set; }

    /// <summary>Clusters successfully added/updated.</summary>
    public int ImportedClusters { get; set; }

    /// <summary>Clusters skipped (no destinations, etc.).</summary>
    public int SkippedClusters { get; set; }

    /// <summary>Per-item errors encountered during import.</summary>
    public List<ImportItemError> Errors { get; set; } = new();

    /// <summary>Optional summary message.</summary>
    public string? Message { get; set; }
}

/// <summary>A single item-level import error.</summary>
public class ImportItemError
{
    /// <summary>&quot;Route&quot; or &quot;Cluster&quot;.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Route ID or Cluster ID.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable reason.</summary>
    public string Error { get; set; } = string.Empty;
}
