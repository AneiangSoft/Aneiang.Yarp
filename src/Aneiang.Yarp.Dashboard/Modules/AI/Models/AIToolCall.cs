namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>
/// Represents a tool call requested by the AI model.
/// </summary>
public class AIToolCall
{
    /// <summary>Unique ID for this tool call (returned by the API).</summary>
    public string Id { get; set; } = "";

    /// <summary>Name of the tool to call.</summary>
    public string ToolName { get; set; } = "";

    /// <summary>JSON string of arguments to pass to the tool.</summary>
    public string Arguments { get; set; } = "{}";
}
