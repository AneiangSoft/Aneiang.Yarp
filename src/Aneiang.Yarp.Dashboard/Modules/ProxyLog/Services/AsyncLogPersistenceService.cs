using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Background service that consumes log entries from ProxyLogStore's persistence Channel
/// and writes batches to SQLite via SqliteProxyLogWriter.
/// </summary>
/// <remarks>
/// Flow: ProxyLogStore.Add() → Channel (DropNewest) → AsyncLogPersistenceService → SqliteProxyLogWriter → SQLite
/// 
/// Features:
/// - Batch writes: accumulates up to 100 entries or flushes every 500ms
/// - Hourly cleanup: deletes expired meta/body rows + WAL checkpoint
/// - DroppedCount: tracks Channel-full drops (also tracked by ProxyLogStore)
/// - WrittenCount: total entries successfully persisted
/// - Implements IProxyLogPersistenceService for DI access to stats
/// </remarks>
public sealed class AsyncLogPersistenceService : IHostedService, IProxyLogPersistenceService
{
    private readonly ProxyLogStore _logStore;
    private readonly SqliteProxyLogWriter _writer;
    private readonly DashboardOptions _options;
    private readonly ILogger<AsyncLogPersistenceService> _logger;
    private long _writtenCount;
    private DateTime _lastCleanup = DateTime.UtcNow;
    private CancellationTokenSource? _cts;
    private Task? _consumeTask;

    public AsyncLogPersistenceService(
        ProxyLogStore logStore,
        SqliteProxyLogWriter writer,
        IOptions<DashboardOptions> options,
        ILogger<AsyncLogPersistenceService> logger)
    {
        _logStore = logStore;
        _writer = writer;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public long DroppedCount => _logStore.DroppedCount;

    /// <inheritdoc />
    public long WrittenCount => Volatile.Read(ref _writtenCount);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.LogPersistenceEnabled)
        {
            _logger.LogInformation("Log persistence is disabled (LogPersistenceEnabled=false)");
            return Task.CompletedTask;
        }

        _logger.LogInformation("AsyncLogPersistenceService starting: meta={MetaDays}d, body={BodyDays}d",
            _options.LogMetaRetentionDays, _options.LogBodyRetentionDays);

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
            catch (Exception ex) { _logger.LogWarning(ex, "AsyncLogPersistenceService consume task stopped with error"); }
        }
        _logger.LogInformation("AsyncLogPersistenceService stopped. Written: {WrittenCount}, Dropped: {DroppedCount}",
            WrittenCount, DroppedCount);
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var reader = _logStore.PersistenceReader;
        var batch = new List<LogEntry>(100);

        while (!ct.IsCancellationRequested)
        {
            batch.Clear();

            try
            {
                // Wait for first entry using WaitToReadAsync — no exception on idle
                if (await reader.WaitToReadAsync(ct))
                {
                    // Drain as many entries as possible (up to 100)
                    while (batch.Count < 100 && reader.TryRead(out var entry))
                        batch.Add(entry);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Service shutting down — write remaining batch if any
                if (batch.Count > 0)
                    await FlushBatchAsync(batch, CancellationToken.None);
                break;
            }

            if (batch.Count > 0)
                await FlushBatchAsync(batch, ct);

            // Hourly cleanup + WAL checkpoint
            if (DateTime.UtcNow - _lastCleanup > TimeSpan.FromHours(1))
            {
                await _writer.CleanupAsync(_options.LogMetaRetentionDays, _options.LogBodyRetentionDays, ct);
                _lastCleanup = DateTime.UtcNow;
            }
        }
    }

    private async Task FlushBatchAsync(List<LogEntry> batch, CancellationToken ct)
    {
        try
        {
            await _writer.WriteBatchAsync(batch, ct);
            Interlocked.Add(ref _writtenCount, batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write {Count} entries to SQLite (will retry in next batch cycle)", batch.Count);
            // Don't throw — persistence is non-critical operational data
        }
    }
}
