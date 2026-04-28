using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Auto-registration hosted service: registers on start, unregisters on stop / 自动注册托管服务：启动时注册，停止时注销.
/// </summary>
internal sealed class GatewayRegistrationHostedService : IHostedService
{
    private readonly GatewayAutoRegistrationClient _client;
    private readonly ILogger<GatewayRegistrationHostedService> _logger;

    public GatewayRegistrationHostedService(GatewayAutoRegistrationClient client, ILogger<GatewayRegistrationHostedService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Auto-registration starting...");
        try
        {
            if (await _client.RegisterAsync(ct).ConfigureAwait(false))
                _logger.LogInformation("Auto-registration complete");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-registration error, service continues");
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
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
}
