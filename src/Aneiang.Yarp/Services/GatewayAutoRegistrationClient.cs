using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Services
{
    /// <summary>
    /// 网关自动注册客户端。
    /// 用于本地服务（如本机调试的单体服务）向远程网关注册路由，
    /// 使流量经过网关的中间件管道后转发到本地服务。
    /// <para>
    /// 配置方式（优先级从高到低）：
    /// <list type="number">
    /// <item>代码：<c>AddAneiangYarpClient(o => o.GatewayUrl = "...")</c></item>
    /// <item>环境变量：<c>GatewayRegistration__GatewayUrl</c></item>
    /// <item>配置文件：<c>appsettings.json</c></item>
    /// </list>
    /// </para>
    /// </summary>
    public class GatewayAutoRegistrationClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly GatewayRegistrationOptions _options;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GatewayAutoRegistrationClient> _logger;

        /// <summary>
        /// 创建网关自动注册客户端。
        /// </summary>
        public GatewayAutoRegistrationClient(
            IHttpClientFactory httpClientFactory,
            IOptions<GatewayRegistrationOptions> options,
            IServiceProvider serviceProvider,
            ILogger<GatewayAutoRegistrationClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// 向网关注册当前服务路由。
        /// </summary>
        public async Task<bool> RegisterAsync(CancellationToken cancellationToken = default)
        {
            if (!_options.IsEnabled)
            {
                _logger.LogInformation("网关自动注册已禁用（未配置 GatewayUrl）");
                return false;
            }

            var gatewayUrl = _options.GatewayUrl;
            var routeName = _options.GetRouteName();
            var clusterName = _options.GetClusterName();
            var matchPath = _options.GetMatchPath();
            var destinationAddress = _options.GetDestinationAddress(_serviceProvider);
            var order = _options.GetOrder();

            if (string.IsNullOrWhiteSpace(gatewayUrl) ||
                string.IsNullOrWhiteSpace(destinationAddress))
            {
                _logger.LogWarning("网关注册信息不完整（GatewayUrl/DestinationAddress 缺失），跳过注册");
                return false;
            }

            _logger.LogInformation("注册信息: Route={RouteName}, Match={MatchPath}, Dest={DestAddress}, Gateway={GatewayUrl}",
                routeName, matchPath, destinationAddress, gatewayUrl);

            // 自动将 localhost 解析为本机内网 IP
            if (_options.GetAutoResolveIp())
                destinationAddress = ResolveLocalAddress(destinationAddress);

            try
            {
                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(_options.GetTimeoutSeconds());

                var requestBody = new
                {
                    routeName,
                    clusterName,
                    matchPath,
                    destinationAddress,
                    order
                };

                var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var registerUrl = $"{gatewayUrl.TrimEnd('/')}/api/gateway/register-route";

                _logger.LogInformation("向网关注册路由 [{RouteName}] {MatchPath} → {Address}",
                    routeName, matchPath, destinationAddress);

                var response = await httpClient.PostAsync(registerUrl, content, cancellationToken)
                    .ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("注册成功 ✓ {Body}", body);
                    return true;
                }

                _logger.LogWarning("注册失败 ({StatusCode}): {Body}", (int)response.StatusCode, body);
                return false;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "网关不可达（{GatewayUrl}），跳过注册", gatewayUrl);
                return false;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("网关请求超时（{GatewayUrl}），跳过注册", gatewayUrl);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册请求出现异常");
                return false;
            }
        }

        /// <summary>
        /// 从网关注销当前路由。
        /// </summary>
        public async Task<bool> UnregisterAsync(CancellationToken cancellationToken = default)
        {
            var gatewayUrl = _options.GatewayUrl;
            var routeName = _options.GetRouteName();

            if (string.IsNullOrWhiteSpace(gatewayUrl))
            {
                _logger.LogWarning("未配置 GatewayUrl，跳过注销");
                return false;
            }

            try
            {
                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(_options.GetTimeoutSeconds());

                var deleteUrl = $"{gatewayUrl.TrimEnd('/')}/api/gateway/{routeName}";

                _logger.LogInformation("向网关注销路由 [{RouteName}]", routeName);
                var response = await httpClient.DeleteAsync(deleteUrl, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
                    _logger.LogInformation("注销成功 ✓ {Body}", body);
                    return true;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("路由 [{RouteName}] 已不存在，忽略", routeName);
                    return true;
                }

                _logger.LogWarning("注销失败 ({StatusCode})", (int)response.StatusCode);
                return false;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "网关不可达，跳过注销");
                return false;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("注销请求超时，跳过注销");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注销请求出现异常");
                return false;
            }
        }

        /// <summary>
        /// 将 destinationAddress 中的 localhost / 127.0.0.1 / 0.0.0.0
        /// 解析为本机内网 IP 地址（使内网网关可以访问本机）。
        /// </summary>
        private static string ResolveLocalAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return address;

            try
            {
                var uri = new Uri(address);
                var host = uri.Host;

                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("127.0.0.1") ||
                    host.Equals("0.0.0.0"))
                {
                    var localIp = GetLocalIpv4();
                    if (localIp != null)
                    {
                        return $"{uri.Scheme}://{localIp}:{uri.Port}{uri.PathAndQuery}";
                    }
                }
            }
            catch (UriFormatException)
            {
                // 如果 address 不是有效 URI，直接原样返回
            }

            return address;
        }

        private static string? GetLocalIpv4()
        {
            try
            {
                var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
                return hostEntry.AddressList
                    .FirstOrDefault(ip =>
                        ip.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip))
                    ?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
