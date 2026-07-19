namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Application;

/// <summary>
/// Provides RAG-style retrieval over the embedded configuration knowledge base.
/// Knowledge entries are stored as embedded JSON resources per feature.
/// </summary>
public interface IConfigKnowledgeService
{
    /// <summary>Search the knowledge base by keyword.</summary>
    Task<KnowledgeResult?> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>Get all knowledge topics.</summary>
    Task<List<KnowledgeEntry>> GetAllTopicsAsync(CancellationToken ct = default);

    /// <summary>Get a specific topic by ID.</summary>
    Task<KnowledgeEntry?> GetTopicAsync(string topicId, CancellationToken ct = default);
}

/// <summary>A single knowledge entry for a configuration topic.</summary>
public class KnowledgeEntry
{
    public string TopicId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Content { get; set; } = "";
    public List<string> KeyPoints { get; set; } = [];
    public List<string> BestPractices { get; set; } = [];
    public List<string> CommonMistakes { get; set; } = [];
    public string? DocUrl { get; set; }
    public string? ExampleConfig { get; set; }
}

/// <summary>Search result from the knowledge base.</summary>
public class KnowledgeResult
{
    public string Query { get; set; } = "";
    public List<KnowledgeSearchHit> Results { get; set; } = [];
}

/// <summary>A single search hit.</summary>
public class KnowledgeSearchHit
{
    public string TopicId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public double RelevanceScore { get; set; }
    public string Snippet { get; set; } = "";
}
