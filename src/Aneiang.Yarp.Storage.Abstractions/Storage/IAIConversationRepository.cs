namespace Aneiang.Yarp.Storage;

/// <summary>
/// Repository for AI conversation history persistence.
/// </summary>
public interface IAIConversationRepository
{
    /// <summary>Save a single conversation message.</summary>
    Task SaveMessageAsync(AIConversationEntry entry, CancellationToken ct = default);

    /// <summary>Get recent messages for a session, ordered chronologically.</summary>
    Task<IReadOnlyList<AIConversationEntry>> GetSessionMessagesAsync(string sessionId, int maxCount = 20, CancellationToken ct = default);

    /// <summary>List all sessions with their last activity time.</summary>
    Task<IReadOnlyList<AISessionSummary>> ListSessionsAsync(int maxCount = 50, CancellationToken ct = default);

    /// <summary>Delete a session and all its messages.</summary>
    Task DeleteSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Delete messages older than the specified time.</summary>
    Task PurgeOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}

/// <summary>A single conversation message entry.</summary>
public class AIConversationEntry
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public string? FunctionCalls { get; set; }
    public string? ToolCallId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>Summary of an AI chat session.</summary>
public class AISessionSummary
{
    public string SessionId { get; set; } = "";
    public int MessageCount { get; set; }
    public DateTime LastActivity { get; set; }
}
