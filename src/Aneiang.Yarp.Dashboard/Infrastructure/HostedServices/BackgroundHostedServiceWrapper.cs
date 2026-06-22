using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.HostedServices;

/// <summary>
/// Wraps an <see cref="IHostedService"/> so that its <see cref="IHostedService.StartAsync"/>
/// runs in a background task instead of blocking the host startup pipeline.
/// This allows Kestrel to bind ports immediately while warmup continues asynchronously.
/// </summary>
internal sealed class BackgroundHostedServiceWrapper : IHostedService
{
    private readonly IHostedService _inner;
    private readonly ILogger<BackgroundHostedServiceWrapper> _logger;
    private readonly string _innerName;
    private Task? _backgroundTask;

    public BackgroundHostedServiceWrapper(IHostedService inner, ILogger<BackgroundHostedServiceWrapper> logger)
    {
        _inner = inner;
        _logger = logger;
        _innerName = inner.GetType().Name;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _backgroundTask = Task.Run(async () =>
        {
            try
            {
                await _inner.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{ServiceName} background startup failed", _innerName);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _inner.StopAsync(cancellationToken).ConfigureAwait(false);
            if (_backgroundTask != null)
            {
                try { await _backgroundTask.WaitAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{ServiceName} stop failed", _innerName);
        }
    }
}
