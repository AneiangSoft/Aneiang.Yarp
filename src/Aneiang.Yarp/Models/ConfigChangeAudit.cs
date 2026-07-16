namespace Aneiang.Yarp.Models;

public class ConfigChangeAudit
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public string? TargetUid { get; set; }
    public string? TargetKeySnapshot { get; set; }
    public string? TargetDisplayNameSnapshot { get; set; }
    public string? Operator { get; set; }
    public string? ClientIp { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
