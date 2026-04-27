using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Services
{
    /// <summary>
    /// 网关自动注册托管服务。
    /// 在应用启动时自动向远程网关注册当前服务的路由，
    /// 在应用停止时自动注销路由。
    /// <para>无需手动调用任何 API，注册为 IHostedService 后自动运行。</para>
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
            _logger.LogInformation("开始网关自动注册...");
            try
            {
                var result = await _client.RegisterAsync(cancellationToken).ConfigureAwait(false);
                if (result)
                    _logger.LogInformation("网关自动注册完成 ✓");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "网关自动注册阶段异常，不影响服务本身运行");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("开始网关自动注销...");
            try
            {
                var result = await _client.UnregisterAsync(cancellationToken).ConfigureAwait(false);
                if (result)
                    _logger.LogInformation("网关自动注销完成 ✓");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "网关自动注销阶段异常，不影响服务本身关闭");
            }
        }
    }
}
