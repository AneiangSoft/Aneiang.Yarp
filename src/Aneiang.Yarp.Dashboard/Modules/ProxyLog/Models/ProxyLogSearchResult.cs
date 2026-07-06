namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

/// <summary>
/// Paginated result for historical proxy log queries from SQLite.
/// Contains lightweight metadata items (no large fields like body/headers).
/// </summary>
public class ProxyLogSearchResult
{
    /// <summary>List of lightweight log metadata items.</summary>
    public List<ProxyLogMetaItem> Items { get; set; } = new();

    /// <summary>Total number of matching records across all pages.</summary>
    public int TotalCount { get; set; }

    /// <summary>Current page number (1-based).</summary>
    public int Page { get; set; }

    /// <summary>Number of items per page.</summary>
    public int PageSize { get; set; }

    /// <summary>Whether there are more pages beyond the current one.</summary>
    public bool HasMore { get; set; }
}
