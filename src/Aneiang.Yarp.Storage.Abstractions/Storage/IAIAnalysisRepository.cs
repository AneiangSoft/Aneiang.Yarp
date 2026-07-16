namespace Aneiang.Yarp.Storage;

public interface IAIAnalysisRepository
{
    Task SaveAnalysisAsync(AIAnalysisEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AIAnalysisEntry>> GetRecentAsync(int maxCount = 20, string? analysisType = null, CancellationToken ct = default);
    Task<IReadOnlyList<AIAnalysisEntry>> GetBySeverityAsync(int minSeverity, int maxCount = 20, CancellationToken ct = default);
    Task PurgeOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
    Task DeleteByIdAsync(long id, CancellationToken ct = default);
}
