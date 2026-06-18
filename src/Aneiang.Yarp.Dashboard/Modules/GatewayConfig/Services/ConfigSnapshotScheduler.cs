using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

public interface IConfigSnapshotScheduler
{
    void QueueSnapshot(string description, string? clientIp = null);
}

/// <summary>
/// Background snapshot scheduler for low-risk mutations.
/// It keeps request latency low while preserving configuration history asynchronously.
/// </summary>
public sealed class ConfigSnapshotScheduler : BackgroundService, IConfigSnapshotScheduler
{
    private readonly IConfigPersistenceService _persistenceService;
    private readonly ILogger<ConfigSnapshotScheduler> _logger;
    private readonly Channel<SnapshotJob> _queue = Channel.CreateBounded<SnapshotJob>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    public ConfigSnapshotScheduler(
        IConfigPersistenceService persistenceService,
        ILogger<ConfigSnapshotScheduler> logger)
    {
        _persistenceService = persistenceService;
        _logger = logger;
    }

    public void QueueSnapshot(string description, string? clientIp = null)
    {
        if (!_queue.Writer.TryWrite(new SnapshotJob(description, clientIp)))
        {
            _logger.LogWarning("Config snapshot queue is full; dropped snapshot request: {Description}", description);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _persistenceService.SaveSnapshotAsync(job.Description, job.ClientIp);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
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
