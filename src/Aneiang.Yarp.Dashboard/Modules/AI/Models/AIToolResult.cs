namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>
/// Result of executing a tool.
/// </summary>
public class AIToolResult
{
    /// <summary>The tool call ID this result corresponds to.</summary>
    public string CallId { get; set; } = "";

    /// <summary>Name of the tool that was executed.</summary>
    public string ToolName { get; set; } = "";

    /// <summary>Whether the tool executed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>JSON-serializable result data.</summary>
    public object? Data { get; set; }

    /// <summary>Error message if the tool failed.</summary>
    public string? ErrorMessage { get; set; }
}
