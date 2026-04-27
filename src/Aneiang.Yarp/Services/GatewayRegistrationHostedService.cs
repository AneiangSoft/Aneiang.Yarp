using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Services
{
    /// <summary>
    /// Gateway auto-registration hosted service.
    /// Automatically registers the current service route with the remote gateway on startup,
    /// and unregisters on shutdown.
    /// <para>No manual API calls needed — runs automatically as an IHostedService.</para>
    /// </summary>
    internal sealed class GatewayRegistrationHostedService : IHostedService
    {
        private readonly GatewayAutoRegistrationClient _client;
        private readonly ILogger<GatewayRegistrationHostedService> _logger;

        public GatewayRegistrationHostedService(
            GatewayAutoRegistrationClient client,
            ILogger<GatewayRegistrationHostedService> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting gateway auto-registration...");
            try
            {
                var result = await _client.RegisterAsync(cancellationToken).ConfigureAwait(false);
                if (result)
                    _logger.LogInformation("Gateway auto-registration complete");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception during gateway auto-registration, service continues running");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting gateway auto-unregistration...");
            try
            {
                var result = await _client.UnregisterAsync(cancellationToken).ConfigureAwait(false);
                if (result)
                    _logger.LogInformation("Gateway auto-unregistration complete");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception during gateway auto-unregistration, service shuts down normally");
            }
        }
    }
}
