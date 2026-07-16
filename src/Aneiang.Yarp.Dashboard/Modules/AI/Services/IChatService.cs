using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Services;

/// <summary>
/// Orchestrates AI chat conversations: builds system prompt with gateway context,
/// manages conversation history, delegates to the AI provider, and handles tool calling.
/// </summary>
public interface IChatService
{
    /// <summary>Build the full system prompt including live gateway context and tool usage instructions.</summary>
    Task<string> BuildSystemPromptAsync(string? locale = null, CancellationToken ct = default);

    /// <summary>Process a user message: save it to the conversation history and return the session id.</summary>
    Task<string> ProcessMessageAsync(
        string? sessionId,
        string userContent,
        CancellationToken ct = default);

    /// <summary>Save an assistant response to the conversation history.</summary>
    Task SaveAssistantResponseAsync(
        string sessionId,
        string content,
        CancellationToken ct = default);

    /// <summary>Build a chat request with conversation history and tool definitions.</summary>
    Task<AIChatRequest> BuildChatRequestAsync(
        string sessionId,
        string? locale = null,
        CancellationToken ct = default);

    /// <summary>Process a chat message with tool-calling support.</summary>
    Task<ChatWithToolsResult> ProcessWithToolsAsync(
        string sessionId,
        AIChatRequest request,
        string? locale = null,
        CancellationToken ct = default);

    /// <summary>Execute a confirmed pending write tool and continue the AI conversation.</summary>
    Task<ChatWithToolsResult> ExecuteConfirmedActionAsync(
        string sessionId,
        AIPendingAction action,
        AIChatRequest originalRequest,
        CancellationToken ct = default);

    /// <summary>Stream a chat response token by token (for non-tool scenarios).</summary>
    IAsyncEnumerable<string> StreamChatAsync(
        string sessionId,
        AIChatRequest request,
        CancellationToken ct = default);
}
