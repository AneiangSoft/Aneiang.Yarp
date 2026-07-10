using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Storage;
using System.Data.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Services;

/// <summary>
/// Background service that consumes WAF security events from WafEventStore's persistence Channel
/// and writes batches to SQLite waf_events_meta table.
/// 
/// Flow: WafEventStore.Add() → Channel (DropNewest) → WafEventPersistenceService → SQLite
/// </summary>
/// <remarks>
/// Features:
/// - Batch writes: accumulates up to 50 events or flushes every 500ms
/// - Cleanup: deletes events older than retention period (default 7 days)
/// - WAL checkpoint after cleanup
/// </remarks>
public sealed class WafEventPersistenceService : IHostedService
{
    private readonly WafEventStore _eventStore;
    private readonly IDbConnectionFactory _connections;
    private readonly ILogger<WafEventPersistenceService> _logger;
    private readonly int _retentionDays;
    private CancellationTokenSource? _cts;
    private Task? _consumeTask;
    private DateTime _lastCleanup = DateTime.UtcNow;
    private long _writtenCount;

    public WafEventPersistenceService(
        WafEventStore eventStore,
        IDbConnectionFactory connections,
        ILogger<WafEventPersistenceService> logger,
        IOptions<DashboardOptions> options)
    {
        _eventStore = eventStore;
        _connections = connections;
        _logger = logger;
        _retentionDays = options.Value.LogMetaRetentionDays; // Use same retention as proxy logs
    }

    public long WrittenCount => Volatile.Read(ref _writtenCount);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("WafEventPersistenceService starting: retention={RetentionDays}d", _retentionDays);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _consumeTask = Task.Run(() => ConsumeLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_consumeTask != null)
        {
            try { await _consumeTask; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogWarning(ex, "WafEventPersistenceService stopped with error"); }
        }
        _logger.LogInformation("WafEventPersistenceService stopped. Written: {WrittenCount}, Dropped: {DroppedCount}",
            WrittenCount, _eventStore.DroppedCount);
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var reader = _eventStore.PersistenceReader;
        var batch = new List<WafSecurityEvent>(50);

        while (!ct.IsCancellationRequested)
        {
            batch.Clear();

            try
            {
                // Wait for first entry using WaitToReadAsync — no exception on idle
                if (await reader.WaitToReadAsync(ct))
                {
                    while (batch.Count < 50 && reader.TryRead(out var evt))
                        batch.Add(evt);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (batch.Count > 0)
                    await FlushBatchAsync(batch, CancellationToken.None);
                break;
            }

            if (batch.Count > 0)
                await FlushBatchAsync(batch, ct);

            // Hourly cleanup + WAL checkpoint
            if (DateTime.UtcNow - _lastCleanup > TimeSpan.FromHours(1))
            {
                await CleanupAsync(ct);
                _lastCleanup = DateTime.UtcNow;
            }
        }
    }

    private async Task FlushBatchAsync(List<WafSecurityEvent> batch, CancellationToken ct)
    {
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();
        try
        {
            foreach (var evt in batch)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandTimeout = 30;
                cmd.CommandText = """
                    INSERT INTO waf_events_meta (EventId, Timestamp, ClientIp, EventType, RuleName, RequestUri, RequestMethod, RouteUid, RouteKeySnapshot, ClusterUid, ClusterKeySnapshot, MatchedValue, Blocked, StatusCode)
                    VALUES (@eid, @ts, @ip, @et, @rn, @uri, @method, @ruid, @rks, @cuid, @cks, @mv, @blk, @sc)
                    """;
                AddParam(cmd, "@eid", evt.Id.ToString("N"));
                AddParam(cmd, "@ts", evt.Timestamp.ToString("O"));
                AddParam(cmd, "@ip", evt.ClientIp);
                AddParam(cmd, "@et", evt.EventType);
                AddParam(cmd, "@rn", evt.RuleName);
                AddParam(cmd, "@uri", (object?)evt.RequestUri ?? DBNull.Value);
                AddParam(cmd, "@method", (object?)evt.RequestMethod ?? DBNull.Value);
                AddParam(cmd, "@ruid", (object?)evt.RouteUid ?? DBNull.Value);
                AddParam(cmd, "@rks", (object?)evt.RouteKeySnapshot ?? DBNull.Value);
                AddParam(cmd, "@cuid", (object?)evt.ClusterUid ?? DBNull.Value);
                AddParam(cmd, "@cks", (object?)evt.ClusterKeySnapshot ?? DBNull.Value);
                AddParam(cmd, "@mv", (object?)evt.MatchedValue ?? DBNull.Value);
                AddParam(cmd, "@blk", evt.Blocked ? 1 : 0);
                AddParam(cmd, "@sc", (object?)evt.StatusCode ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            Interlocked.Add(ref _writtenCount, batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write {Count} WAF events to SQLite", batch.Count);
            try { await tx.RollbackAsync(CancellationToken.None); } catch { }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = _connections.CreateConnection();
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 60;
            cmd.CommandText = "DELETE FROM waf_events_meta WHERE Timestamp < datetime('now', @days || ' days')";
            AddParam(cmd, "@days", $"-{_retentionDays}");
            var deleted = await cmd.ExecuteNonQueryAsync(ct);

            // WAL checkpoint
            await using var ckptCmd = conn.CreateCommand();
            ckptCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            await ckptCmd.ExecuteNonQueryAsync(ct);

            _logger.LogInformation("WAF event cleanup: deleted {Deleted} rows older than {Days} days", deleted, _retentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WAF event cleanup failed (non-critical, will retry next cycle)");
        }
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
