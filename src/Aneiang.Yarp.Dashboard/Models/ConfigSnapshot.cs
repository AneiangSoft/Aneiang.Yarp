using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Models;

/// <summary>
/// Configuration snapshot for YARP rollback/history.
/// Stores full YARP config as JSON for restore.
/// </summary>
public class ConfigSnapshot
{
    /// <summary>Unique version identifier.</summary>
    public string VersionId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Snapshot creation time.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Snapshot description.</summary>
    public string? Description { get; set; }

    /// <summary>Client IP that triggered the snapshot.</summary>
    public string? ClientIp { get; set; }

    /// <summary>Full YARP configuration as JSON.</summary>
    public JsonElement Config { get; set; }
}
