namespace Aneiang.Yarp.Storage;

/// <summary>
/// Repository for AI-generated analysis results (log summaries, anomaly detections, suggestions).
/// </summary>
public interface IAIAnalysisRepository
{
    /// <summary>Save an analysis result.</summary>
    Task SaveAnalysisAsync(AIAnalysisEntry entry, CancellationToken ct = default);

    /// <summary>Get recent analysis results, newest first.</summary>
    Task<IReadOnlyList<AIAnalysisEntry>> GetRecentAsync(int maxCount = 20, string? analysisType = null, CancellationToken ct = default);

    /// <summary>Get analysis results with severity >= the specified level.</summary>
    Task<IReadOnlyList<AIAnalysisEntry>> GetBySeverityAsync(int minSeverity, int maxCount = 20, CancellationToken ct = default);

    /// <summary>Delete analysis results older than the specified time.</summary>
    Task PurgeOlderThanAsync(DateTime cutoff, CancellationToken ct = default);

    /// <summary>Delete a specific analysis result by ID.</summary>
    Task DeleteByIdAsync(long id, CancellationToken ct = default);
}

/// <summary>An AI-generated analysis entry.</summary>
public class AIAnalysisEntry
{
    public long Id { get; set; }
    public string AnalysisType { get; set; } = ""; // log_summary / anomaly / suggestion
    public string Content { get; set; } = "";
    public int Severity { get; set; }
    public string? RelatedRoutes { get; set; }
    public string? RelatedClusters { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
