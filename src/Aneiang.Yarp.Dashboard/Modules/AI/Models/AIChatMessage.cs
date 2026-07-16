namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>
/// Represents a single message in an AI conversation.
/// </summary>
public class AIChatMessage
{
    public string Role { get; set; } = "user"; // system / user / assistant / tool
    public string Content { get; set; } = "";

    /// <summary>Optional function call result (for function calling).</summary>
    public string? FunctionName { get; set; }
    public string? FunctionArguments { get; set; }
    public string? FunctionResult { get; set; }

    /// <summary>Tool call ID (for tool response messages with role="tool").</summary>
    public string? ToolCallId { get; set; }

    /// <summary>Tool calls made by the assistant (for assistant messages with tool_calls).</summary>
    public List<AIToolCall>? ToolCalls { get; set; }
}
