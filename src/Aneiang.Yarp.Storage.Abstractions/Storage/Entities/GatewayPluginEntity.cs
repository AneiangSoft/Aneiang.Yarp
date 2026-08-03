namespace Aneiang.Yarp.Storage;

/// <summary>Persisted installation and runtime registration state for a gateway plugin.</summary>
public sealed class GatewayPluginEntity
{
    public string PluginId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool IsBuiltIn { get; set; }
    public string? SourcePath { get; set; }
    public string RegistrationStatus { get; set; } = "installed";
    public string? LastError { get; set; }
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
