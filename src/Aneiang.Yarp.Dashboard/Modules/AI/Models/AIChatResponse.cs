namespace Aneiang.Yarp.Dashboard.Modules.AI;

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
