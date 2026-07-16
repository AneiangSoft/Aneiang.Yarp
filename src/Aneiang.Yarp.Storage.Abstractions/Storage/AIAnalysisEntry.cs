namespace Aneiang.Yarp.Storage;

public class AIAnalysisEntry
{
    public long Id { get; set; }
    public string AnalysisType { get; set; } = "";
    public string Content { get; set; } = "";
    public int Severity { get; set; }
    public string? RelatedRoutes { get; set; }
    public string? RelatedClusters { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
