using System.Threading.Channels;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

public interface IConfigSnapshotScheduler
{
    bool QueueSnapshot(string description, string? clientIp = null);
    ConfigSnapshotMetrics GetMetrics();
}

/// <summary>
/// Background snapshot scheduler for low-risk mutations.
/// It keeps request latency low while preserving configuration history asynchronously.
/// </summary>
public sealed class ConfigSnapshotScheduler : BackgroundService, IConfigSnapshotScheduler
{
    private readonly IConfigPersistenceService _persistenceService;
    private readonly ILogger<ConfigSnapshotScheduler> _logger;
    private readonly Channel<SnapshotJob> _queue;
    private int _queueLength;
    private long _enqueuedCount;
    private long _processedCount;
    private long _failedCount;
    private long _droppedCount;

    public ConfigSnapshotScheduler(
        IConfigPersistenceService persistenceService,
        IOptions<ConfigHistoryOptions> options,
        ILogger<ConfigSnapshotScheduler> logger)
    {
        _persistenceService = persistenceService;
        _logger = logger;
        var capacity = Math.Max(1, options.Value.SnapshotQueueCapacity);
        _queue = Channel.CreateBounded<SnapshotJob>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
    }

    public bool QueueSnapshot(string description, string? clientIp = null)
    {
        if (!_queue.Writer.TryWrite(new SnapshotJob(description, clientIp)))
        {
            Interlocked.Increment(ref _droppedCount);
            _logger.LogWarning("Config snapshot queue is full; dropped snapshot request: {Description}", description);
            return false;
        }

        Interlocked.Increment(ref _queueLength);
        Interlocked.Increment(ref _enqueuedCount);
        return true;
    }

    public ConfigSnapshotMetrics GetMetrics() => new(
        Math.Max(0, Volatile.Read(ref _queueLength)),
        Interlocked.Read(ref _enqueuedCount),
        Interlocked.Read(ref _processedCount),
        Interlocked.Read(ref _failedCount),
        Interlocked.Read(ref _droppedCount));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            Interlocked.Decrement(ref _queueLength);
            try
            {
                await _persistenceService.SaveSnapshotAsync(job.Description, job.ClientIp);
                Interlocked.Increment(ref _processedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedCount);
                _logger.LogWarning(ex, "Failed to save queued config snapshot: {Description}", job.Description);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    private sealed record SnapshotJob(string Description, string? ClientIp);
}
