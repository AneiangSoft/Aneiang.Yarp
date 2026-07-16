using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Storage.Sqlite;

public sealed class SqliteProxyLogRepository : IProxyLogRepository
{
    private readonly SqliteConnectionFactory _connections;
    private readonly SqliteProxyLogBatchWriter _writer;
    private readonly SqliteProxyLogReader _reader;
    private readonly SqliteProxyLogAggregator _aggregator;

    public SqliteProxyLogRepository(SqliteConnectionFactory connections, ILogger<SqliteProxyLogRepository> logger)
    {
        _connections = connections;
        _writer = new SqliteProxyLogBatchWriter(connections, logger);
        _reader = new SqliteProxyLogReader(connections, _writer);
        _aggregator = new SqliteProxyLogAggregator(connections, _writer);
    }

    public async Task WriteBatchAsync(
        IEnumerable<ProxyLogMetaEntity> metaEntries,
        IEnumerable<ProxyLogBodyEntity> bodyEntries,
        CancellationToken ct = default)
    {
        await _writer.EnsureInitializedAsync(ct);
        await _writer.WriteBatchAsync(metaEntries, bodyEntries, ct);
    }

    public async Task<ProxyLogBodyEntity?> GetBodyByMetaIdAsync(long metaId, CancellationToken ct = default)
    {
        await _writer.EnsureInitializedAsync(ct);
        return await _reader.GetBodyByMetaIdAsync(metaId, ct);
    }

    public async Task<ProxyLogMetaEntity?> GetMetaByIdAsync(long metaId, CancellationToken ct = default)
    {
        await _writer.EnsureInitializedAsync(ct);
        return await _reader.GetMetaByIdAsync(metaId, ct);
    }

    public async Task<(List<ProxyLogMetaEntity> Items, int TotalCount)> SearchAsync(
        int page, int pageSize,
        string? routeId = null, string? clusterId = null, string? level = null,
        int? statusCodeMin = null, int? statusCodeMax = null,
        DateTime? startTime = null, DateTime? endTime = null,
        string? keyword = null, string? eventType = null,
        CancellationToken ct = default)
    {
        await _writer.EnsureInitializedAsync(ct);
        return await _reader.SearchAsync(page, pageSize, routeId, clusterId, level,
            statusCodeMin, statusCodeMax, startTime, endTime, keyword, eventType, ct);
    }

    public async Task CleanupAsync(int metaRetentionDays, int bodyRetentionDays, CancellationToken ct = default)
    {
        await _writer.EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();
        try
        {
            await using var metaCmd = conn.CreateCommand();
            metaCmd.Transaction = tx;
            metaCmd.CommandTimeout = 60;
            metaCmd.CommandText = "DELETE FROM proxy_logs_meta WHERE Timestamp < datetime('now', @metaDays || ' days')";
            metaCmd.Parameters.AddWithValue("@metaDays", $"-{metaRetentionDays}");
            await metaCmd.ExecuteNonQueryAsync(ct);

            await using var bodyCmd = conn.CreateCommand();
            bodyCmd.Transaction = tx;
            bodyCmd.CommandTimeout = 60;
            bodyCmd.CommandText = """
                DELETE FROM proxy_logs_body
                WHERE MetaId IN (
                    SELECT m.Id FROM proxy_logs_meta m
                    WHERE m.Timestamp < datetime('now', @bodyDays || ' days')
                )
                """;
            bodyCmd.Parameters.AddWithValue("@bodyDays", $"-{bodyRetentionDays}");
            await bodyCmd.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task CheckpointAsync(CancellationToken ct = default)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ProxyLogStatsResult> GetStatsAsync(int recentMinutes, CancellationToken ct = default)
    {
        await _writer.EnsureInitializedAsync(ct);
        return await _aggregator.GetStatsAsync(recentMinutes, ct);
    }

    public async Task<List<ProxyLogTrafficBucket>> GetTrafficDataAsync(DateTime startTime, CancellationToken ct = default)
    {
        await _writer.EnsureInitializedAsync(ct);
        return await _aggregator.GetTrafficDataAsync(startTime, ct);
    }

    public async Task<List<ProxyLogRouteIssue>> GetTopIssuesAsync(DateTime startTime, int count, CancellationToken ct = default)
    {
        await _writer.EnsureInitializedAsync(ct);
        return await _aggregator.GetTopIssuesAsync(startTime, count, ct);
    }

    public async Task<int> GetRecent5xxCountAsync(int minutes, CancellationToken ct = default)
    {
        await _writer.EnsureInitializedAsync(ct);
        return await _aggregator.GetRecent5xxCountAsync(minutes, ct);
    }
}
