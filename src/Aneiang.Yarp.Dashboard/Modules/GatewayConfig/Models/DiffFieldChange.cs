namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

/// <summary>Details of a field-level change.</summary>
public class DiffFieldChange
{
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
