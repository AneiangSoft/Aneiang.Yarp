namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>
/// Result of processing a chat message with tool calling.
/// </summary>
public class ChatWithToolsResult
{
    public ChatResultType Type { get; set; }
    public string Text { get; set; } = "";
    public AIPendingAction? PendingAction { get; set; }
    public AIToolResult? ToolResult { get; set; }

    /// <summary>
    /// Accumulated conversation messages (including tool results) after the tool loop.
    /// When set, the controller should make a final streaming call with these messages.
    /// </summary>
    public List<AIChatMessage>? AccumulatedMessages { get; set; }

    /// <summary>The original request (preserved for action continuation).</summary>
    internal AIChatRequest? PendingRequest { get; set; }
}
