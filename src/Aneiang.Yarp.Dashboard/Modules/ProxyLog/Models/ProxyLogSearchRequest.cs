namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

/// <summary>
/// Search/filter parameters for querying historical proxy logs from SQLite.
/// Used by the /api/logs/history endpoint for paginated log retrieval.
/// </summary>
public class ProxyLogSearchRequest
{
    /// <summary>Page number (1-based). Default: 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Number of items per page. Default: 50, max: 200.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Filter by route ID (exact match).</summary>
    public string? RouteId { get; set; }

    /// <summary>Filter by cluster ID (exact match).</summary>
    public string? ClusterId { get; set; }

    /// <summary>Filter by log level: Debug, Information, Warning, Error, Critical.</summary>
    public string? Level { get; set; }

    /// <summary>Minimum status code filter (e.g. 400 for errors).</summary>
    public int? StatusCodeMin { get; set; }

    /// <summary>Maximum status code filter.</summary>
    public int? StatusCodeMax { get; set; }

    /// <summary>Start time for time range filter (ISO 8601).</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>End time for time range filter (ISO 8601).</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>Keyword search in upstream path and message (LIKE pattern).</summary>
    public string? Keyword { get; set; }

    /// <summary>Filter by event type: ProxyRequest or ProxyResponse.</summary>
    public string? EventType { get; set; }
}
