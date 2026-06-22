using System.Diagnostics;
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
        _logger.LogInformation("[HostedService] {ServiceName} starting in background...", _innerName);
        var sw = Stopwatch.StartNew();

        _backgroundTask = Task.Run(async () =>
        {
            try
            {
                await _inner.StartAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("[HostedService] {ServiceName} background start completed in {ElapsedMs}ms", _innerName, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[HostedService] {ServiceName} background startup failed after {ElapsedMs}ms", _innerName, sw.ElapsedMilliseconds);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[HostedService] {ServiceName} stopping...", _innerName);
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.StopAsync(cancellationToken).ConfigureAwait(false);
            if (_backgroundTask != null)
            {
                try { await _backgroundTask.WaitAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            _logger.LogInformation("[HostedService] {ServiceName} stopped in {ElapsedMs}ms", _innerName, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HostedService] {ServiceName} stop failed after {ElapsedMs}ms", _innerName, sw.ElapsedMilliseconds);
        }
    }
}
