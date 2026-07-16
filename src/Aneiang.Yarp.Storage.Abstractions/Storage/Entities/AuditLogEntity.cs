namespace Aneiang.Yarp.Storage;

public class AuditLogEntity
{
    public string Id { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? TargetType { get; set; } // Route, Cluster, Policy, etc.
    public string? TargetUid { get; set; }
    public string? TargetKeySnapshot { get; set; }
    public string? TargetDisplayNameSnapshot { get; set; }
    public string? Operator { get; set; }
    public string? ClientIp { get; set; }
    public string? BeforeData { get; set; } // JSON
    public string? AfterData { get; set; } // JSON
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
