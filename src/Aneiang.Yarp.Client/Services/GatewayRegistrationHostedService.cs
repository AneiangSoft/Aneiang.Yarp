using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Auto-registration hosted service: registers on start (with exponential backoff retry),
/// sends periodic heartbeat, unregisters on stop.
/// </summary>
internal sealed class GatewayRegistrationHostedService : IHostedService
{
    private readonly GatewayAutoRegistrationClient _client;
    private readonly ILogger<GatewayRegistrationHostedService> _logger;
    private Timer? _heartbeatTimer;

    private const int MaxRetries = 5;
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16),
        TimeSpan.FromSeconds(30)
    };

    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);

    public GatewayRegistrationHostedService(GatewayAutoRegistrationClient client, ILogger<GatewayRegistrationHostedService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Auto-registration starting...");

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                if (await _client.RegisterAsync(ct).ConfigureAwait(false))
                {
                    _logger.LogInformation("Auto-registration complete");
                    StartHeartbeat(ct);
                    return;
                }

                // Non-exception failure (e.g. gateway returned error), still retry
                if (attempt < MaxRetries - 1)
                {
                    var delay = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
                    _logger.LogInformation(
                        "Registration attempt {Attempt}/{MaxRetries} failed, retrying in {Delay}s...",
                        attempt + 1, MaxRetries, delay.TotalSeconds);
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("Auto-registration cancelled during shutdown");
                return;
            }
            catch (Exception ex)
            {
                if (attempt < MaxRetries - 1)
                {
                    var delay = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
                    _logger.LogWarning(ex,
                        "Registration attempt {Attempt}/{MaxRetries} failed, retrying in {Delay}s...",
                        attempt + 1, MaxRetries, delay.TotalSeconds);
                    try
                    {
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
                else
                {
                    _logger.LogWarning(ex,
                        "Auto-registration failed after {MaxRetries} attempts, service continues without gateway registration",
                        MaxRetries);
                }
            }
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        // Stop heartbeat
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;

        _logger.LogInformation("Auto-unregistration starting...");
        try
        {
            if (await _client.UnregisterAsync(ct).ConfigureAwait(false))
                _logger.LogInformation("Auto-unregistration complete");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-unregistration error, service shuts down normally");
        }
    }

    /// <summary>
    /// Start periodic heartbeat to keep the registration alive.
    /// Gateway can detect stale registrations if heartbeat stops.
    /// </summary>
    private void StartHeartbeat(CancellationToken ct)
    {
        _heartbeatTimer = new Timer(async _ =>
        {
            try
            {
                await _client.HeartbeatAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown, stop heartbeat
                _heartbeatTimer?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat failed");
            }
        }, null, DefaultHeartbeatInterval, DefaultHeartbeatInterval);

        _logger.LogInformation("Heartbeat started (interval: {Interval}s)", DefaultHeartbeatInterval.TotalSeconds);
    }
}
