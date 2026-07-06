using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Storage.Entities;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Service for querying and managing proxy logs.
/// Supports both in-memory real-time queries and SQLite historical queries.
/// </summary>
public interface IDashboardLogQueryService
{
    /// <summary>
    /// Gets recent log entries from the in-memory ring buffer (real-time display).
    /// </summary>
    /// <param name="count">Maximum number of entries to return.</param>
    /// <returns>Log store snapshot.</returns>
    ProxyLogStoreSnapshot GetLogs(int count = 100);

    /// <summary>Gets paged log entries from the in-memory ring buffer.</summary>
    ProxyLogStoreSnapshot GetLogsPage(int page = 1, int pageSize = 100);

    /// <summary>
    /// Clears all log entries from the in-memory ring buffer.
    /// </summary>
    void ClearLogs();

    /// <summary>
    /// Query historical log metadata from SQLite with pagination and filtering.
    /// Only reads from proxy_logs_meta — no large fields loaded.
    /// </summary>
    Task<ProxyLogSearchResult> GetHistoryLogsAsync(ProxyLogSearchRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get a single log detail (full body/headers) from SQLite proxy_logs_body table.
    /// Called when a user expands a row in the UI.
    /// </summary>
    Task<ProxyLogDetailResult?> GetLogDetailAsync(long metaId, CancellationToken ct = default);
}
