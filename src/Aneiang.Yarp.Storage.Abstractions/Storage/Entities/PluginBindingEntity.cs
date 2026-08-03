namespace Aneiang.Yarp.Storage.Entities;

/// <summary>Persisted plugin configuration bound to a route or cluster.</summary>
public sealed class PluginBindingEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PluginId { get; set; } = string.Empty;
    public string PluginVersion { get; set; } = string.Empty;
    public PluginBindingScope Scope { get; set; }
    /// <summary>Legacy mutable RouteId/ClusterId retained for compatibility with existing databases and clients.</summary>
    public string ScopeId { get; set; } = string.Empty;
    public string? RouteUid { get; set; }
    public string? ClusterUid { get; set; }
    public bool Enabled { get; set; } = true;
    public string ConfigJson { get; set; } = "{}";
    public int SchemaVersion { get; set; } = 1;
    public long ConfigVersion { get; set; } = 1;
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Supported gateway configuration binding scopes.</summary>
public enum PluginBindingScope
{
    Route = 1,
    Cluster = 2
}
