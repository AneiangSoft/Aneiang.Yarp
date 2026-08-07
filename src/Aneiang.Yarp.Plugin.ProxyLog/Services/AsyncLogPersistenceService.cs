using Aneiang.Yarp.Infrastructure.Middleware;
using Aneiang.Yarp.Plugins;
using Aneiang.Yarp.Plugin.ProxyLog.Models;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace Aneiang.Yarp.Plugin.ProxyLog.Services;

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
public sealed class AsyncLogPersistenceService : IProxyLogPersistenceService, IPluginRuntimeResource
{
    private readonly ProxyLogStore _logStore;
    private readonly SqliteProxyLogWriter _writer;
    private readonly ProxyLogRuntimeSettings _runtimeSettings;
    private readonly IGatewayPluginManager _pluginManager;
    private readonly ILogger<AsyncLogPersistenceService> _logger;
    private long _writtenCount;
    private DateTime _lastCleanup = DateTime.Now;
    private CancellationTokenSource? _cts;
    private Task? _consumeTask;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _stoppedAt;
    private Exception? _lastError;
    private long _failureCount;

    public string PluginId => "proxy-log";
    public string ResourceId => "proxy-log:channel-sqlite-writer";
    public string ResourceType => "channel-writer";

    public AsyncLogPersistenceService(
        ProxyLogStore logStore,
        SqliteProxyLogWriter writer,
        ProxyLogRuntimeSettings runtimeSettings,
        IGatewayPluginManager pluginManager,
        ILogger<AsyncLogPersistenceService> logger)
    {
        _logStore = logStore;
        _writer = writer;
        _runtimeSettings = runtimeSettings;
        _pluginManager = pluginManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public long DroppedCount => _logStore.DroppedCount;

    /// <inheritdoc />
    public long WrittenCount => Volatile.Read(ref _writtenCount);

    public ValueTask StartResourceAsync(CancellationToken cancellationToken)
    {
        if (_consumeTask is { IsCompleted: false }) return ValueTask.CompletedTask;
        var settings = _runtimeSettings.Current;
        _logger.LogInformation("AsyncLogPersistenceService starting: enabled={Enabled}, meta={MetaDays}d, body={BodyDays}d",
            settings.PersistenceEnabled, settings.MetaRetentionDays, settings.BodyRetentionDays);
        _lastError = null;
        _startedAt = DateTimeOffset.UtcNow;
        _cts = new CancellationTokenSource();
        _consumeTask = Task.Run(() => ConsumeLoopAsync(_cts.Token), CancellationToken.None);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopResourceAsync(CancellationToken cancellationToken)
    {
        if (_consumeTask is null) return;
        _cts?.Cancel();
        try { await _consumeTask.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _lastError = ex;
            Interlocked.Increment(ref _failureCount);
            _logger.LogWarning(ex, "AsyncLogPersistenceService consume task stopped with error");
        }
        _cts?.Dispose();
        _cts = null;
        _consumeTask = null;
        _stoppedAt = DateTimeOffset.UtcNow;
        _logger.LogInformation("AsyncLogPersistenceService stopped. Written: {WrittenCount}, Dropped: {DroppedCount}", WrittenCount, DroppedCount);
    }

    public ValueTask<PluginRuntimeResourceSnapshot> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var running = _consumeTask is { IsCompleted: false };
        var health = _lastError is not null ? PluginResourceHealthStatus.Faulted :
            running ? PluginResourceHealthStatus.Healthy : PluginResourceHealthStatus.Stopped;
        return ValueTask.FromResult(new PluginRuntimeResourceSnapshot(ResourceId, ResourceType, running, health,
            _startedAt, _stoppedAt, _lastError?.Message, new Dictionary<string, long>
            {
                ["writtenEntries"] = WrittenCount,
                ["droppedEntries"] = DroppedCount,
                ["failures"] = Interlocked.Read(ref _failureCount),
                ["memoryBytes"] = _logStore.GetEstimatedMemoryBytes()
            }));
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
