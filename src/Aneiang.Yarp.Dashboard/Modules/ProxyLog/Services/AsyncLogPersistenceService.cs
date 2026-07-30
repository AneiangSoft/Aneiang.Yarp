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
/// Flow: ProxyLogStore.Add() → bounded Channel → AsyncLogPersistenceService → SqliteProxyLogWriter → SQLite
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
    private readonly ProxyLogRuntimeSettings _runtimeSettings;
    private readonly ILogger<AsyncLogPersistenceService> _logger;
    private long _writtenCount;
    private DateTime _lastCleanup = DateTime.Now;
    private CancellationTokenSource? _cts;
    private Task? _consumeTask;

    public AsyncLogPersistenceService(
        ProxyLogStore logStore,
        SqliteProxyLogWriter writer,
        ProxyLogRuntimeSettings runtimeSettings,
        ILogger<AsyncLogPersistenceService> logger)
    {
        _logStore = logStore;
        _writer = writer;
        _runtimeSettings = runtimeSettings;
        _logger = logger;
    }

    /// <inheritdoc />
    public long DroppedCount => _logStore.DroppedCount;

    /// <inheritdoc />
    public long WrittenCount => Volatile.Read(ref _writtenCount);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = _runtimeSettings.Current;
        _logger.LogInformation("AsyncLogPersistenceService starting: enabled={Enabled}, meta={MetaDays}d, body={BodyDays}d",
            settings.PersistenceEnabled, settings.MetaRetentionDays, settings.BodyRetentionDays);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _consumeTask = Task.Run(() => ConsumeLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logStore.CompletePersistence();
        if (_consumeTask != null)
        {
            try { await _consumeTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                _cts?.Cancel();
                try { await _consumeTask; } catch (OperationCanceledException) { }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "AsyncLogPersistenceService consume task stopped with error"); }
        }
        _logger.LogInformation("AsyncLogPersistenceService stopped. Written: {WrittenCount}, Dropped: {DroppedCount}",
            WrittenCount, DroppedCount);
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        const int batchSize = 100;
        var reader = _logStore.PersistenceReader;
        var batch = new List<LogEntry>(batchSize);

        try
        {
            while (await reader.WaitToReadAsync(ct))
            {
                batch.Clear();
                if (!reader.TryRead(out var first))
                    continue;

                batch.Add(first);
                using var flushDelay = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                while (batch.Count < batchSize)
                {
                    while (batch.Count < batchSize && reader.TryRead(out var entry))
                        batch.Add(entry);

                    if (batch.Count >= batchSize || flushDelay.IsCancellationRequested)
                        break;

                    try
                    {
                        if (!await reader.WaitToReadAsync(flushDelay.Token))
                            break;
                    }
                    catch (OperationCanceledException) when (flushDelay.IsCancellationRequested)
                    {
                        break;
                    }
                }

                await FlushBatchAsync(batch, ct);

                if (DateTime.Now - _lastCleanup > TimeSpan.FromHours(1))
                {
                    var settings = _runtimeSettings.Current;
                    await _writer.CleanupAsync(settings.MetaRetentionDays, settings.BodyRetentionDays, ct);
                    _lastCleanup = DateTime.Now;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Forced shutdown after graceful drain timed out.
        }
        finally
        {
            batch.Clear();
            while (reader.TryRead(out var entry))
            {
                batch.Add(entry);
                if (batch.Count < batchSize)
                    continue;

                await FlushBatchAsync(batch, CancellationToken.None);
                batch.Clear();
            }

            if (batch.Count > 0)
                await FlushBatchAsync(batch, CancellationToken.None);
        }
    }

    private async Task FlushBatchAsync(IReadOnlyCollection<LogEntry> batch, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await _writer.WriteBatchAsync(batch, ct);
                Interlocked.Add(ref _writtenCount, batch.Count);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < 3)
            {
                _logger.LogWarning(ex,
                    "Failed to write {Count} entries to SQLite (attempt {Attempt}/3)",
                    batch.Count, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to write {Count} entries to SQLite after 3 attempts; batch dropped",
                    batch.Count);
            }
        }
    }
}
