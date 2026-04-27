using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aneiang.Yarp.Models
{
    /// <summary>
    /// 网关自动注册配置选项。
    /// <para>
    /// 支持三种配置方式（优先级从高到低）：
    /// <list type="number">
    /// <item>代码：<c>builder.Services.AddAneiangYarpClient(o => o.GatewayUrl = "...")</c></item>
    /// <item>环境变量：<c>GatewayRegistration__GatewayUrl</c></item>
    /// <item>配置文件：<c>appsettings.json → GatewayRegistration</c></item>
    /// </list>
    /// </para>
    /// </summary>
    public class GatewayRegistrationOptions
    {
        /// <summary>
        /// JSON 配置节名称
        /// </summary>
        public const string SectionName = "GatewayRegistration";

        /// <summary>
        /// 是否启用自动注册。
        /// <para>智能默认：如果设置了 <see cref="GatewayUrl"/> 则自动启用；否则禁用。</para>
        /// </summary>
        public bool? Enabled { get; set; }

        /// <summary>
        /// 网关服务地址，例如 http://192.168.1.100:5000
        /// <para><b>必填（唯一必须配置的字段）。</b></para>
        /// </summary>
        public string? GatewayUrl { get; set; }

        /// <summary>
        /// 路由名称（在网关上的唯一标识）。
        /// <para>默认：使用入口程序集名称（如 "MyService"）。</para>
        /// </summary>
        public string? RouteName { get; set; }

        /// <summary>
        /// 集群名称。
        /// <para>默认：与 <see cref="RouteName"/> 相同。</para>
        /// </summary>
        public string? ClusterName { get; set; }

        /// <summary>
        /// 匹配路径模板，如 /api/my-service/{**catch-all}
        /// <para>默认："/{**catch-all}"（转发所有路径）。</para>
        /// </summary>
        public string? MatchPath { get; set; }

        /// <summary>
        /// 本机目标地址，如 http://localhost:5001
        /// <para>
        /// 默认：自动从 Kestrel 绑定的第一个地址获取。
        /// 如果默认值为 localhost/127.0.0.1，会在注册时<b>自动解析为本机内网 IP</b>。
        /// </para>
        /// </summary>
        public string? DestinationAddress { get; set; }

        /// <summary>
        /// 路由优先级，数值越小越优先。
        /// <para>默认：50。</para>
        /// </summary>
        public int? Order { get; set; }

        /// <summary>
        /// 在注册前自动将 localhost/127.0.0.1/0.0.0.0 解析为本机内网 IPv4。
        /// <para>默认：true。</para>
        /// </summary>
        public bool? AutoResolveIp { get; set; }

        /// <summary>
        /// HTTP 超时（秒）。<para>默认：5。</para>
        /// </summary>
        public int? TimeoutSeconds { get; set; }

        /// <summary>
        /// 启用实例隔离模式（多人调试同一服务时使用）。
        /// <para>
        /// 开启后自动在本机标识到路由和路径中，多人同时调试互不影响：
        /// </para>
        /// <list type="bullet">
        /// <item>routeName → "{routeName}-{instanceId}"</item>
        /// <item>clusterName → "{clusterName}-{instanceId}"</item>
        /// <item>matchPath → "/{instanceId}{matchPath}"（路径级别隔离）</item>
        /// </list>
        /// <para><b>默认：true</b></para>
        /// </summary>
        public bool? InstanceIsolation { get; set; }

        /// <summary>
        /// 自定义实例标识符（仅 <see cref="InstanceIsolation"/>=true 时生效）。
        /// <para>默认：本机主机名（Environment.MachineName）</para>
        /// </summary>
        public string? InstanceId { get; set; }

        /// <summary>
        /// 实例隔离的路径前缀格式。
        /// <para>可用占位符：{instanceId}、{machineName}、{userName}</para>
        /// <para>默认："{instanceId}"</para>
        /// </summary>
        public string? InstancePrefixFormat { get; set; }

        // ── 智能默认值解析 ──

        internal bool IsEnabled => Enabled ?? !string.IsNullOrWhiteSpace(GatewayUrl);

        private bool IsInstanceIsolation() => InstanceIsolation != false;

        private string ResolveInstanceId()
        {
            if (!string.IsNullOrWhiteSpace(InstanceId))
                return InstanceId;
            return Environment.MachineName;
        }

        private string GetPrefix()
        {
            if (!IsInstanceIsolation())
                return string.Empty;

            var format = !string.IsNullOrWhiteSpace(InstancePrefixFormat)
                ? InstancePrefixFormat
                : "{instanceId}";

            var instanceId = ResolveInstanceId();
            return format
                .Replace("{instanceId}", instanceId)
                .Replace("{machineName}", Environment.MachineName)
                .Replace("{userName}", Environment.UserName);
        }

        internal string GetRouteName()
        {
            var name = RouteName;
            if (string.IsNullOrWhiteSpace(name))
                name = Assembly.GetEntryAssembly()?.GetName().Name ?? "my-service";

            var prefix = GetPrefix();
            return !string.IsNullOrEmpty(prefix) ? $"{name}-{prefix}" : name;
        }

        internal string GetClusterName()
        {
            var name = ClusterName;
            if (string.IsNullOrWhiteSpace(name))
                return GetRouteName();

            var prefix = GetPrefix();
            return !string.IsNullOrEmpty(prefix) ? $"{name}-{prefix}" : name;
        }

        internal string GetMatchPath()
        {
            var path = MatchPath;
            if (string.IsNullOrWhiteSpace(path))
                path = "/{**catch-all}";

            var prefix = GetPrefix();
            if (string.IsNullOrEmpty(prefix))
                return path;

            // 确保 path 以 / 开头
            if (!path.StartsWith("/"))
                path = "/" + path;

            return $"/{prefix}{path}";
        }

        internal int GetOrder() => Order ?? 50;

        internal bool GetAutoResolveIp() => AutoResolveIp ?? true;

        internal int GetTimeoutSeconds() => TimeoutSeconds ?? 5;

        internal string? GetDestinationAddress(IServiceProvider sp)
        {
            if (!string.IsNullOrWhiteSpace(DestinationAddress))
                return DestinationAddress;

            // 自动从 Kestrel 绑定地址获取
            var config = sp.GetRequiredService<IConfiguration>();
            var urls = config["urls"] ?? config["Urls"];

            if (!string.IsNullOrWhiteSpace(urls))
            {
                // 取第一个地址（去掉分号分隔的多个地址）
                var firstUrl = urls.Split(';')[0].Trim();
                if (firstUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return firstUrl;
            }

            // 兜底：常见的 ASP.NET Core 默认地址
            var env = sp.GetRequiredService<IHostEnvironment>();
            var defaultPort = env.IsDevelopment() ? "5001" : "5000";
            return $"http://localhost:{defaultPort}";
        }
    }
}
