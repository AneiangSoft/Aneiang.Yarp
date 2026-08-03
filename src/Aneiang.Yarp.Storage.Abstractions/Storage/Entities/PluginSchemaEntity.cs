namespace Aneiang.Yarp.Storage.Entities;

/// <summary>Versioned JSON Schema published by a gateway plugin.</summary>
public sealed class PluginSchemaEntity
{
    public string PluginId { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string SchemaJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
