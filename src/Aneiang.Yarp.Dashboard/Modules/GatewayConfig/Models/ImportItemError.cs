namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

/// <summary>A single item-level import error.</summary>
public class ImportItemError
{
    /// <summary>"Route" or "Cluster".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Route ID or Cluster ID.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable reason.</summary>
    public string Error { get; set; } = string.Empty;
}
