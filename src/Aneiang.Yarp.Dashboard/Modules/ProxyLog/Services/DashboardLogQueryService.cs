using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Implementation of dashboard log query service.
/// Supports both in-memory real-time queries and SQLite historical queries.
/// Real-time: reads from ProxyLogStore ring buffer (max 64 entries).
/// Historical: reads from proxy_logs_meta SQLite table (paginated, filtered).
/// Detail: reads from proxy_logs_body SQLite table (single row, large fields).
/// </summary>
internal sealed class DashboardLogQueryService : IDashboardLogQueryService
{
    private readonly ProxyLogStore _logStore;
    private readonly IProxyLogRepository _logRepository;
    private readonly DashboardOptions _options;

    public DashboardLogQueryService(
        ProxyLogStore logStore,
        IProxyLogRepository logRepository,
        IOptions<DashboardOptions> options)
    {
        _logStore = logStore;
        _logRepository = logRepository;
        _options = options.Value;
    }

    /// <inheritdoc />
    public ProxyLogStoreSnapshot GetLogs(int count = 100)
    {
        if (!_options.EnableProxyLogging)
        {
            return new ProxyLogStoreSnapshot
            {
                Entries = new List<LogEntry>(),
                BufferSize = 0,
                EvictedCount = 0,
                BufferCapacity = _options.LogBufferCapacity
            };
        }

        return _logStore.GetRecent(count);
    }

    /// <inheritdoc />
    public ProxyLogStoreSnapshot GetLogsPage(int page = 1, int pageSize = 100)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, Math.Max(1, _options.LogBufferCapacity));

        if (!_options.EnableProxyLogging)
        {
            return new ProxyLogStoreSnapshot
            {
                Entries = new List<LogEntry>(),
                BufferSize = 0,
                EvictedCount = 0,
                BufferCapacity = _options.LogBufferCapacity,
                Page = page,
                PageSize = pageSize,
                Total = 0,
                TotalPages = 0
            };
        }

        var snapshot = _logStore.GetRecent(_options.LogBufferCapacity);
        var total = snapshot.BufferSize;
        snapshot.Entries = snapshot.Entries
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        snapshot.Page = page;
        snapshot.PageSize = pageSize;
        snapshot.Total = total;
        snapshot.TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return snapshot;
    }

    /// <inheritdoc />
    public void ClearLogs()
    {
        if (_options.EnableProxyLogging)
        {
            _logStore.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<ProxyLogSearchResult> GetHistoryLogsAsync(ProxyLogSearchRequest request, CancellationToken ct = default)
    {
        if (!_options.LogPersistenceEnabled)
            return new ProxyLogSearchResult { Items = new(), TotalCount = 0, Page = request.Page, PageSize = request.PageSize, HasMore = false };

        var (items, totalCount) = await _logRepository.SearchAsync(
            request.Page, request.PageSize,
            request.RouteId, request.ClusterId, request.Level,
            request.StatusCodeMin, request.StatusCodeMax,
            request.StartTime, request.EndTime,
            request.Keyword, request.EventType, ct);

        var metaItems = items.Select(MapToMetaItem).ToList();
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)request.PageSize);

        return new ProxyLogSearchResult
        {
            Items = metaItems,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            HasMore = request.Page < totalPages
        };
    }

    /// <inheritdoc />
    public async Task<ProxyLogDetailResult?> GetLogDetailAsync(long metaId, CancellationToken ct = default)
    {
        if (!_options.LogPersistenceEnabled) return null;

        var meta = await _logRepository.GetMetaByIdAsync(metaId, ct);
        if (meta == null) return null;

        var body = await _logRepository.GetBodyByMetaIdAsync(metaId, ct);

        return new ProxyLogDetailResult
        {
            Id = meta.Id,
            Timestamp = DateTime.Parse(meta.Timestamp),
            EventType = meta.EventType,
            Level = meta.Level,
            RouteId = meta.RouteId,
            ClusterId = meta.ClusterId,
            Method = meta.Method,
            UpstreamPath = meta.UpstreamPath,
            StatusCode = meta.StatusCode,
            ElapsedMs = meta.ElapsedMs,
            TraceId = meta.TraceId,
            HasRequestBody = meta.HasRequestBody != 0,
            HasResponseBody = meta.HasResponseBody != 0,
            DownstreamUrl = meta.DownstreamUrl,
            // Body fields
            Message = body?.Message,
            RequestBody = body?.RequestBody,
            ResponseBody = body?.ResponseBody,
            RequestHeaders = DeserializeHeaders(body?.RequestHeaders),
            ResponseHeaders = DeserializeHeaders(body?.ResponseHeaders),
            DownstreamBody = body?.DownstreamBody,
            Exception = body?.Exception
        };
    }

    private static ProxyLogMetaItem MapToMetaItem(ProxyLogMetaEntity meta) => new()
    {
        Id = meta.Id,
        Timestamp = DateTime.Parse(meta.Timestamp),
        EventType = meta.EventType,
        Level = meta.Level,
        RouteId = meta.RouteId,
        ClusterId = meta.ClusterId,
        Method = meta.Method,
        UpstreamPath = meta.UpstreamPath,
        StatusCode = meta.StatusCode,
        ElapsedMs = meta.ElapsedMs,
        TraceId = meta.TraceId,
        HasRequestBody = meta.HasRequestBody != 0,
        HasResponseBody = meta.HasResponseBody != 0,
        DownstreamUrl = meta.DownstreamUrl
    };

    private static HeaderList? DeserializeHeaders(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<HeaderList>(json); }
        catch { return null; }
    }
}
