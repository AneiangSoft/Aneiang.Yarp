namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

/// <summary>Result of comparing two config snapshots.</summary>
public class ConfigDiffResult
{
    public string FromVersion { get; set; } = string.Empty;
    public string ToVersion { get; set; } = string.Empty;
    public DateTime ComparedAt { get; set; } = DateTime.Now;
    public DiffSummary Summary { get; set; } = new();
    public List<DiffEntry> Changes { get; set; } = new();
}
