namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>
/// Pending action proposed by the AI that requires user confirmation.
/// </summary>
public class AIPendingAction
{
    /// <summary>Tool call ID.</summary>
    public string CallId { get; set; } = "";

    /// <summary>Tool name.</summary>
    public string ToolName { get; set; } = "";

    /// <summary>JSON arguments.</summary>
    public string Arguments { get; set; } = "{}";

    /// <summary>Human-readable description of what will happen.</summary>
    public string Description { get; set; } = "";
}
