namespace Aneiang.Yarp.Storage.Entities;

/// <summary>A saved plugin configuration preset that can be applied to any binding of the same plugin.</summary>
public class PluginConfigPresetEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public int SchemaVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
