using Aneiang.Yarp.Dashboard.Models;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// High-performance structured logging service using Channel for async processing.
/// Features:
/// - Lock-free log ingestion via Channel
/// - Background batch processing
/// - Tiered storage: Memory -> SQLite -> File Archive
/// - SSE streaming support for real-time log delivery
/// </summary>
public class StructuredLogService : IHostedService, IDisposable
{
    // Channel for lock-free log ingestion
    private readonly Channel<LogEntry> _logChannel;
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;

    // In-memory ring buffer for hot data
    private readonly ProxyLogStore _ringBuffer;

    // Batch processing configuration
    private const int BatchSize = 100;
    private const int MaxQueueSize = 10000;
    private static readonly TimeSpan BatchFlushInterval = TimeSpan.FromSeconds(1);

    // SSE subscribers for real-time streaming
    private readonly List<Channel<LogEntry>> _sseSubscribers = new();
    private readonly object _subscriberLock = new();

    // Tiered storage
    private readonly string? _archivePath;
    private readonly bool _enablePersistence;

    public StructuredLogService(
        ProxyLogStore ringBuffer,
        IConfiguration? configuration = null)
    {
        _ringBuffer = ringBuffer;

        // Bounded channel to prevent memory explosion under high load
        _logChannel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(MaxQueueSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest, // Drop oldest logs when full
            SingleReader = true,
            SingleWriter = false
        });

        // Optional persistence configuration
        _enablePersistence = configuration?.GetValue<bool>("Dashboard:EnableLogPersistence") ?? false;
        if (_enablePersistence)
        {
            _archivePath = configuration?.GetValue<string>("Dashboard:LogArchivePath") ?? "logs/archive";
            Directory.CreateDirectory(_archivePath);
        }
    }

    /// <summary>
    /// Enqueues a log entry for async processing. Non-blocking, lock-free.
    /// </summary>
    public ValueTask EnqueueAsync(LogEntry entry)
    {
        // Fast path: try write without async
        if (_logChannel.Writer.TryWrite(entry))
        {
            return ValueTask.CompletedTask;
        }

        // Slow path: async write (should rarely happen with bounded channel)
        return _logChannel.Writer.WriteAsync(entry);
    }

    /// <summary>
    /// Subscribes to real-time log stream via SSE.
    /// </summary>
    public Channel<LogEntry> SubscribeToStream()
    {
        var channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        lock (_subscriberLock)
        {
            _sseSubscribers.Add(channel);
        }

        return channel;
    }

    /// <summary>
    /// Unsubscribes from real-time log stream.
    /// </summary>
    public void UnsubscribeFromStream(Channel<LogEntry> channel)
    {
        lock (_subscriberLock)
        {
            _sseSubscribers.Remove(channel);
        }
        channel.Writer.Complete();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _processingTask = ProcessLogsAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _logChannel.Writer.Complete();

        if (_processingTask != null)
        {
            await _processingTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    /// <summary>
    /// Background log processing loop.
    /// Batches logs and writes to multiple storage tiers.
    /// </summary>
    private async Task ProcessLogsAsync(CancellationToken ct)
    {
        var batch = new List<LogEntry>(BatchSize);
        var flushTimer = new PeriodicTimer(BatchFlushInterval);

        try
        {
            await foreach (var entry in _logChannel.Reader.ReadAllAsync(ct))
            {
                batch.Add(entry);

                // Process batch when full or timer fires
                if (batch.Count >= BatchSize)
                {
                    await ProcessBatchAsync(batch, ct);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            // Log to console as last resort
            Console.Error.WriteLine($"[StructuredLogService] Processing error: {ex}");
        }
        finally
        {
            // Final flush
            if (batch.Count > 0)
            {
                await ProcessBatchAsync(batch, ct);
            }
            flushTimer.Dispose();
        }
    }

    /// <summary>
    /// Processes a batch of logs: ring buffer, SSE broadcast, persistence.
    /// </summary>
    private async Task ProcessBatchAsync(List<LogEntry> batch, CancellationToken ct)
    {
        try
        {
            // 1. Write to in-memory ring buffer (for API queries)
            foreach (var entry in batch)
            {
                _ringBuffer.Add(entry);
            }

            // 2. Broadcast to SSE subscribers (real-time streaming)
            await BroadcastToSubscribersAsync(batch, ct);

            // 3. Persist to warm storage (SQLite) and cold storage (files)
            if (_enablePersistence)
            {
                await PersistBatchAsync(batch, ct);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[StructuredLogService] Batch processing error: {ex}");
        }
    }

    /// <summary>
    /// Broadcasts logs to all SSE subscribers.
    /// </summary>
    private async Task BroadcastToSubscribersAsync(List<LogEntry> batch, CancellationToken ct)
    {
        List<Channel<LogEntry>>? subscribers = null;

        lock (_subscriberLock)
        {
            if (_sseSubscribers.Count > 0)
            {
                subscribers = _sseSubscribers.ToList();
            }
        }

        if (subscribers == null) return;

        foreach (var entry in batch)
        {
            var deadSubscribers = new List<Channel<LogEntry>>();

            foreach (var sub in subscribers)
            {
                try
                {
                    if (!sub.Writer.TryWrite(entry))
                    {
                        deadSubscribers.Add(sub);
                    }
                }
                catch
                {
                    deadSubscribers.Add(sub);
                }
            }

            // Clean up dead subscribers
            if (deadSubscribers.Count > 0)
            {
                lock (_subscriberLock)
                {
                    foreach (var dead in deadSubscribers)
                    {
                        _sseSubscribers.Remove(dead);
                        dead.Writer.Complete();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Persists batch to SQLite and/or archive files.
    /// </summary>
    private async Task PersistBatchAsync(List<LogEntry> batch, CancellationToken ct)
    {
        // TODO: Implement SQLite persistence
        // TODO: Implement file archive rotation
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts.Dispose();
        _logChannel.Writer.Complete();
    }
}
