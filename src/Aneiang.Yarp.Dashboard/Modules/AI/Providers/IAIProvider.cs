namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>
/// Unified abstraction for LLM API providers.
/// All providers (OpenAI, DeepSeek, Qwen, etc.) implement this interface.
/// </summary>
public interface IAIProvider
{
    /// <summary>Whether the provider is properly configured and ready to serve requests.</summary>
    bool IsAvailable { get; }

    /// <summary>Provider display name (e.g. "OpenAI", "DeepSeek").</summary>
    string ProviderName { get; }

    /// <summary>Send a chat completion request and get the full response.</summary>
    Task<AIChatResponse> ChatAsync(AIChatRequest request, CancellationToken ct = default);

    /// <summary>Send a chat completion request with streaming (SSE) response.</summary>
    IAsyncEnumerable<string> ChatStreamAsync(AIChatRequest request, CancellationToken ct = default);
}
