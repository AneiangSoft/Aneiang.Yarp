namespace Aneiang.Yarp.Models;

/// <summary>
/// Audit log entry for gateway configuration changes.
/// </summary>
public class ConfigChangeAudit
{
    /// <summary>Unique identifier for this audit entry.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Action type: AddRoute, UpdateRoute, RemoveRoute, AddCluster, UpdateCluster, RemoveCluster, RenameCluster, Rollback, ReplaceAll.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Target of the action (e.g. route name, cluster ID).</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target type.
    /// </summary>
    public string? TargetType { get; set; }
    /// <summary>
    /// Gets or sets the target uid.
    /// </summary>
    public string? TargetUid { get; set; }
    /// <summary>
    /// Gets or sets the target key snapshot.
    /// </summary>
    public string? TargetKeySnapshot { get; set; }
    /// <summary>
    /// Gets or sets the target display name snapshot.
    /// </summary>
    public string? TargetDisplayNameSnapshot { get; set; }

    /// <summary>Who initiated the change (e.g. "auto-register", "dashboard-user", "API").</summary>
    public string? Operator { get; set; }

    /// <summary>Client IP of the requester.</summary>
    public string? ClientIp { get; set; }

    /// <summary>Configuration state before the change (JSON).</summary>
    public string? Before { get; set; }

    /// <summary>Configuration state after the change (JSON).</summary>
    public string? After { get; set; }

    /// <summary>Whether the operation succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Error message if the operation failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Timestamp of the change (UTC).</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
