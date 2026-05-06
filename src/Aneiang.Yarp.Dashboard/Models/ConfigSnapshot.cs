using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Models;

/// <summary>Configuration snapshot for version management and rollback.</summary>
public class ConfigSnapshot
{
    /// <summary>Unique version identifier.</summary>
    public string VersionId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Snapshot creation timestamp.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Optional description of this snapshot.</summary>
    public string? Description { get; set; }

    /// <summary>Complete configuration content.</summary>
    public JsonElement Config { get; set; }
}
