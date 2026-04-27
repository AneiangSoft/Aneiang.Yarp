using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Extensions
{
    /// <summary>
    /// Aneiang.Yarp 应用中间件扩展方法
    /// </summary>
    public static class YarpApplicationExtensions
    {
        /// <summary>
        /// 网关自动注册：应用启动时自动向配置的网关服务注册路由信息，适用于本地开发环境（网关未启动时会静默忽略注册错误）
        /// </summary>
        /// <param name="app"></param>
        /// <returns></returns>
        public static IApplicationBuilder UseAneiangYarpGateway(this IApplicationBuilder app)
        {
            return app.UseAneiangYarpGatewayAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 网关自动注册：应用启动时自动向配置的网关服务注册路由信息，适用于本地开发环境（网关未启动时会静默忽略注册错误）
        /// </summary>
        /// <param name="app"></param>
        /// <returns></returns>
        public static async Task<IApplicationBuilder> UseAneiangYarpGatewayAsync(this IApplicationBuilder app)
        {
            var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
            var config = configuration.GetSection("GatewayRegistration");

            var enabled = config.GetValue<bool>("Enabled");
            if (!enabled)
            {
                Console.WriteLine("[GatewayRegister] 网关自动注册已禁用 (Enabled=false)");
                return app;
            }

            var gatewayUrl = config["GatewayUrl"];
            var routeName = config["RouteName"];
            var clusterName = config["ClusterName"];
            var matchPath = config["MatchPath"];
            var destinationAddress = config["DestinationAddress"];
            var order = config.GetValue<int?>("Order");

            // 校验必填字段
            if (string.IsNullOrWhiteSpace(gatewayUrl) ||
                string.IsNullOrWhiteSpace(routeName) ||
                string.IsNullOrWhiteSpace(clusterName) ||
                string.IsNullOrWhiteSpace(matchPath) ||
                string.IsNullOrWhiteSpace(destinationAddress))
            {
                Console.WriteLine("[GatewayRegister] 配置不完整，跳过注册。请检查 appsettings.json 中的 GatewayRegistration 节");
                return app;
            }

            // 自动解析本机内网IP，替换 destinationAddress 中的 localhost/127.0.0.1/0.0.0.0
            // 因为网关不能通过 localhost 转发到开发机
            destinationAddress = ResolveDestinationAddress(destinationAddress);

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

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
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var gatewayApiUrl = $"{gatewayUrl.TrimEnd('/')}/api/gateway/register-route";

                Console.WriteLine($"[GatewayRegister] 正在向网关注册路由: {routeName} ({matchPath} -> {destinationAddress})");

                var response = await httpClient.PostAsync(gatewayApiUrl, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("[GatewayRegister] 注册成功: {responseBody}");
                }
                else
                {
                    Console.WriteLine($"[GatewayRegister] 注册失败 ({(int)response.StatusCode}): {responseBody}");
                }
            }
            catch (HttpRequestException ex)
            {
                // 网关未启动时静默忽略（本地开发场景）
                Console.WriteLine($"[GatewayRegister] 网关不可达({ex.Message})，跳过注册（本地开发可忽略）");
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("[GatewayRegister] 网关请求超时，跳过注册（本地开发可忽略）");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GatewayRegister] 注册请求异常:{ex.Message}");
            }

            return app;
        }

        /// <summary>
        /// 解析目标地址，将 localhost / 127.0.0.1 / 0.0.0.0 替换为本机内网IP
        /// </summary>
        private static string ResolveDestinationAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return address;

            var uri = new Uri(address);
            var host = uri.Host;

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("127.0.0.1") ||
                host.Equals("0.0.0.0"))
            {
                var localIp = GetLocalIpAddress();
                if (!string.IsNullOrEmpty(localIp))
                {
                    var resolved = $"{uri.Scheme}://{localIp}:{uri.Port}{uri.PathAndQuery}";
                    return resolved;
                }
            }

            return address;
        }

        /// <summary>
        /// 获取本机第一个非回环的 IPv4 地址（内网IP）
        /// </summary>
        private static string? GetLocalIpAddress()
        {
            try
            {
                var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
                return hostEntry.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    ?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
