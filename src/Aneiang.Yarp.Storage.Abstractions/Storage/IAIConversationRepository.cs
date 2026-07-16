namespace Aneiang.Yarp.Storage;

public interface IAIConversationRepository
{
    Task SaveMessageAsync(AIConversationEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AIConversationEntry>> GetSessionMessagesAsync(string sessionId, int maxCount = 20, CancellationToken ct = default);
    Task<IReadOnlyList<AISessionSummary>> ListSessionsAsync(int maxCount = 50, CancellationToken ct = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken ct = default);
    Task PurgeOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
