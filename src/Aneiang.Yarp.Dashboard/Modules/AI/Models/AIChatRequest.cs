namespace Aneiang.Yarp.Dashboard.Modules.AI;

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
