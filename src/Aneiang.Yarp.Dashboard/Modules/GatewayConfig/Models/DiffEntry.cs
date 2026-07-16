namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

/// <summary>Individual change entry in a diff.</summary>
public class DiffEntry
{
    public DiffChangeType ChangeType { get; set; }
    public DiffEntityType EntityType { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public string? ParentId { get; set; }

    /// <summary>All field-level changes for this entity.</summary>
    public List<DiffFieldChange> FieldChanges { get; set; } = new();

    /// <summary>Human-readable description of the change.</summary>
    public string Description { get; set; } = string.Empty;
}
