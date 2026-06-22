using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.HostedServices;

/// <summary>
/// Diagnostic wrapper that logs enter/exit and elapsed time for an inner IHostedService.
/// Use this to identify which hosted service is hanging during startup.
/// </summary>
internal sealed class TimedHostedService : IHostedService
{
    private readonly IHostedService _inner;
    private readonly ILogger<TimedHostedService> _logger;
    private readonly string _name;

    public TimedHostedService(IHostedService inner, ILogger<TimedHostedService> logger)
    {
        _inner = inner;
        _logger = logger;
        _name = inner.GetType().Name;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[HostedService] {ServiceName} starting...", _name);
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.StartAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("[HostedService] {ServiceName} started in {ElapsedMs}ms", _name, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HostedService] {ServiceName} failed after {ElapsedMs}ms", _name, sw.ElapsedMilliseconds);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[HostedService] {ServiceName} stopping...", _name);
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.StopAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("[HostedService] {ServiceName} stopped in {ElapsedMs}ms", _name, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HostedService] {ServiceName} stop failed after {ElapsedMs}ms", _name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
