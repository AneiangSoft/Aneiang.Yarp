using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// High-level batch writer that converts LogEntry objects into ProxyLogMetaEntity + ProxyLogBodyEntity
/// and delegates the actual SQLite write to IProxyLogRepository.WriteBatchAsync().
/// 
/// This service sits between the AsyncLogPersistenceService (Channel consumer) and
/// the IProxyLogRepository (SQLite data access layer), handling the LogEntry → Entity mapping.
/// </summary>
public sealed class SqliteProxyLogWriter
{
    private readonly IProxyLogRepository _repository;
    private readonly ILogger<SqliteProxyLogWriter> _logger;
    private long _writtenCount;

    public SqliteProxyLogWriter(IProxyLogRepository repository, ILogger<SqliteProxyLogWriter> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>Total number of log entries successfully written to SQLite.</summary>
    public long WrittenCount => Volatile.Read(ref _writtenCount);

    /// <summary>
    /// Convert a batch of LogEntry objects to entity pairs and write to SQLite.
    /// Each LogEntry is split into a lightweight meta row and a body row (if it has large fields).
    /// </summary>
    public async Task WriteBatchAsync(IEnumerable<LogEntry> entries, CancellationToken ct = default)
    {
        var metaList = new List<ProxyLogMetaEntity>();
        var bodyList = new List<ProxyLogBodyEntity>();

        foreach (var entry in entries)
        {
            var meta = MapToMeta(entry);
            var body = MapToBody(entry);
            metaList.Add(meta);
            if (body != null)
                bodyList.Add(body);
        }

        if (metaList.Count == 0) return;

        try
        {
            await _repository.WriteBatchAsync(metaList, bodyList, ct);
            Interlocked.Add(ref _writtenCount, metaList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write batch of {Count} log entries to SQLite", metaList.Count);
            throw;
        }
    }

    /// <summary>
    /// Clean up expired log records and run WAL checkpoint.
    /// </summary>
    public async Task CleanupAsync(int metaRetentionDays, int bodyRetentionDays, CancellationToken ct = default)
    {
        try
        {
            await _repository.CleanupAsync(metaRetentionDays, bodyRetentionDays, ct);
            await _repository.CheckpointAsync(ct);
            _logger.LogInformation("Log cleanup completed: meta={MetaDays}d, body={BodyDays}d", metaRetentionDays, bodyRetentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Log cleanup failed (non-critical, will retry on next cycle)");
        }
    }

    private static ProxyLogMetaEntity MapToMeta(LogEntry entry) => new()
    {
        Timestamp = entry.Timestamp.ToString("O"),
        EventType = entry.EventType.ToString(),
        Level = entry.Level,
        RouteId = entry.RouteId,
        ClusterId = entry.ClusterId,
        Method = entry.Method,
        UpstreamPath = entry.UpstreamPath,
        StatusCode = entry.StatusCode ?? 0,
        ElapsedMs = entry.ElapsedMs ?? 0,
        TraceId = entry.TraceId,
        HasRequestBody = entry.RequestBody != null ? 1 : 0,
        HasResponseBody = entry.ResponseBody != null ? 1 : 0,
        DownstreamUrl = entry.DownstreamUrl
    };

    private static ProxyLogBodyEntity? MapToBody(LogEntry entry)
    {
        // Only create a body row if there are large fields to store
        if (entry.RequestBody == null &&
            entry.ResponseBody == null &&
            entry.RequestHeaders == null &&
            entry.ResponseHeaders == null &&
            entry.DownstreamBody == null &&
            entry.Exception == null &&
            string.IsNullOrEmpty(entry.Message))
        {
            return null;
        }

        return new ProxyLogBodyEntity
        {
            MetaId = 0, // Will be set by repository after meta insert
            Message = entry.Message,
            RequestBody = entry.RequestBody,
            ResponseBody = entry.ResponseBody,
            RequestHeaders = SerializeHeaders(entry.RequestHeaders),
            ResponseHeaders = SerializeHeaders(entry.ResponseHeaders),
            DownstreamBody = entry.DownstreamBody,
            Exception = entry.Exception
        };
    }

    private static string? SerializeHeaders(HeaderList? headers)
    {
        if (headers == null || headers.Count == 0) return null;
        return JsonSerializer.Serialize(headers);
    }
}
