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

/// <summary>
/// Request payload for AI chat completion.
/// </summary>
public class AIChatRequest
{
    /// <summary>Conversation messages (oldest first).</summary>
    public List<AIChatMessage> Messages { get; set; } = new();

    /// <summary>Model override (null = use default from AIOptions).</summary>
    public string? Model { get; set; }

    /// <summary>System prompt override.</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Temperature override (null = use default from AIOptions).</summary>
    public double? Temperature { get; set; }

    /// <summary>Max tokens override (null = use default from AIOptions).</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Available tools for function calling (OpenAI tools format).</summary>
    public List<AIToolDefinition>? Tools { get; set; }
}

/// <summary>
/// Response from AI chat completion.
/// </summary>
public class AIChatResponse
{
    public string Content { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }

    /// <summary>Tokens consumed by this request (for budget tracking).</summary>
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }

    /// <summary>If the model requested a function call (legacy format).</summary>
    public string? FunctionName { get; set; }
    public string? FunctionArguments { get; set; }

    /// <summary>Tool calls requested by the model (OpenAI tools format, supports multiple calls).</summary>
    public List<AIToolCall>? ToolCalls { get; set; }

    /// <summary>Whether the response contains any tool call requests.</summary>
    public bool HasToolCalls => (ToolCalls?.Count > 0) || !string.IsNullOrEmpty(FunctionName);

    public static AIChatResponse Ok(string content, int promptTokens = 0, int completionTokens = 0)
        => new() { Content = content, Success = true, PromptTokens = promptTokens, CompletionTokens = completionTokens };

    public static AIChatResponse Fail(string error)
        => new() { Success = false, Error = error };
}

/// <summary>
/// Definition of a tool available for AI function calling.
/// Uses OpenAI tools/function-calling standard format.
/// </summary>
public class AIToolDefinition
{
    /// <summary>Tool name (e.g. "get_routes", "create_route").</summary>
    public string Name { get; set; } = "";

    /// <summary>Human-readable description for the AI.</summary>
    public string Description { get; set; } = "";

    /// <summary>JSON Schema describing the tool parameters.</summary>
    public object Parameters { get; set; } = new { type = "object", properties = new { }, required = Array.Empty<string>() };

    /// <summary>Whether this tool is read-only (safe to auto-execute without user confirmation).</summary>
    public bool IsReadOnly { get; set; }
}

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

/// <summary>Result type for chat-with-tools processing.</summary>
public enum ChatResultType
{
    /// <summary>Plain text response (no pending action).</summary>
    Text,
    /// <summary>Write operation needs user confirmation.</summary>
    PendingAction,
    /// <summary>Error occurred.</summary>
    Error
}

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

/// <summary>Chat request DTO from frontend.</summary>
public class ChatRequestDto
{
    public string? SessionId { get; set; }
    public List<ChatMessageDto>? Messages { get; set; }
    /// <summary>Current UI locale (e.g. "en-US", "zh-CN") from the frontend.</summary>
    public string? Locale { get; set; }
}

/// <summary>Single chat message from frontend.</summary>
public class ChatMessageDto
{
    public string Role { get; set; } = "user";
    public string? Content { get; set; }
}

/// <summary>Confirm action request DTO.</summary>
public class ConfirmActionDto
{
    public string SessionId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string? Arguments { get; set; }
    public string? CallId { get; set; }
    /// <summary>Current UI locale from the frontend.</summary>
    public string? Locale { get; set; }
}
