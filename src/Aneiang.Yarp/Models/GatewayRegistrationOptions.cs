using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aneiang.Yarp.Models;

/// <summary>
/// Gateway auto-registration options / 网关自动注册配置.
/// Config sources (priority high to low): code > env vars > appsettings.json.
/// 配置来源（优先级从高到低）：代码 > 环境变量 > appsettings.json.
/// </summary>
public class GatewayRegistrationOptions
{
    /// <summary>JSON config section name / JSON 配置节点.</summary>
    public const string SectionName = "Gateway:Registration";

    /// <summary>Auto-enabled when <see cref="GatewayUrl"/> is set / 设置 GatewayUrl 时自动启用.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Gateway URL, e.g. http://192.168.1.100:5000. Required field / 网关地址，必填.</summary>
    public string? GatewayUrl { get; set; }

    /// <summary>Route name (default: entry assembly name) / 路由名称（默认：入口程序集名）.</summary>
    public string? RouteName { get; set; }

    /// <summary>Cluster name (default: same as RouteName) / 集群名称（默认：同 RouteName）.</summary>
    public string? ClusterName { get; set; }

    /// <summary>Match path template, e.g. /api/my-service/{**catch-all}. Default: /{**catch-all} / 匹配路径模板.</summary>
    public string? MatchPath { get; set; }

    /// <summary>
    /// Destination address, e.g. http://localhost:5001.
    /// Default: auto-detected from Kestrel binding; localhost is auto-resolved to LAN IP.
    /// 目标地址（默认：自动从 Kestrel 绑定地址获取，localhost 自动替换为局域网 IP）.
    /// </summary>
    public string? DestinationAddress { get; set; }

    /// <summary>Route priority — lower = higher precedence. Default: 50 / 路由优先级，越小越优先.</summary>
    public int? Order { get; set; }

    /// <summary>Auto-resolve localhost/127.0.0.1/0.0.0.0 to LAN IPv4. Default: true / 自动解析本地地址为局域网 IP.</summary>
    public bool? AutoResolveIp { get; set; }

    /// <summary>HTTP timeout in seconds. Default: 5 / HTTP 超时（秒）.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Instance isolation for multi-developer debugging.
    /// Embeds machine name into route/cluster/path so instances don't conflict. Default: true.
    /// 多开发者实例隔离模式：将机器名嵌入路由/集群/路径以区分实例.
    /// </summary>
    public bool? InstanceIsolation { get; set; }

    /// <summary>Custom instance ID (default: Environment.MachineName) / 自定义实例 ID.</summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// Instance prefix format. Placeholders: {instanceId}, {machineName}, {userName}. Default: "{instanceId}".
    /// 实例前缀模板.
    /// </summary>
    public string? InstancePrefixFormat { get; set; }

    /// <summary>Bearer token for gateway API auth / Bearer 令牌.</summary>
    public string? AuthToken { get; set; }

    /// <summary>API Key for gateway API auth. Sent as X-Api-Key header / API 密钥.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Basic auth username. Must pair with <see cref="BasicAuthPassword"/> / Basic 认证用户名.</summary>
    public string? BasicAuthUsername { get; set; }

    /// <summary>Basic auth password. Must pair with <see cref="BasicAuthUsername"/> / Basic 认证密码.</summary>
    public string? BasicAuthPassword { get; set; }

    // ── Smart defaults / 智能默认值 ─────────────────────────

    internal bool IsEnabled => Enabled ?? !string.IsNullOrWhiteSpace(GatewayUrl);

    private bool IsInstanceIsolation() => InstanceIsolation != false;

    private string ResolveInstanceId() =>
        !string.IsNullOrWhiteSpace(InstanceId) ? InstanceId : Environment.MachineName;

    private string GetPrefix()
    {
        if (!IsInstanceIsolation()) return string.Empty;
        var fmt = !string.IsNullOrWhiteSpace(InstancePrefixFormat) ? InstancePrefixFormat : "{instanceId}";
        var id = ResolveInstanceId();
        return fmt.Replace("{instanceId}", id)
                  .Replace("{machineName}", Environment.MachineName)
                  .Replace("{userName}", Environment.UserName);
    }

    internal string GetRouteName()
    {
        var prefix = GetPrefix();
        var name = !string.IsNullOrWhiteSpace(RouteName)
            ? RouteName : Assembly.GetEntryAssembly()?.GetName().Name ?? "my-service";
        return !string.IsNullOrEmpty(prefix) ? $"{name}-{prefix}" : name;
    }

    internal string GetClusterName()
    {
        var prefix = GetPrefix();
        // Use RouteName as base (without prefix), then append prefix once
        var baseName = !string.IsNullOrWhiteSpace(ClusterName)
            ? ClusterName
            : (!string.IsNullOrWhiteSpace(RouteName) ? RouteName : Assembly.GetEntryAssembly()?.GetName().Name ?? "my-service");
        return !string.IsNullOrEmpty(prefix) ? $"{baseName}-cluster-{prefix}" : baseName;
    }

    internal string GetMatchPath()
    {
        var path = !string.IsNullOrWhiteSpace(MatchPath) ? MatchPath : "/{**catch-all}";
        if (!path.StartsWith("/")) path = "/" + path;
        var prefix = GetPrefix();
        return !string.IsNullOrEmpty(prefix) ? $"/{prefix}{path}" : path;
    }

    internal int GetOrder() => Order ?? 50;
    internal bool GetAutoResolveIp() => AutoResolveIp ?? true;
    internal int GetTimeoutSeconds() => TimeoutSeconds ?? 5;

    internal string? GetDestinationAddress(IServiceProvider sp)
    {
        if (!string.IsNullOrWhiteSpace(DestinationAddress)) return DestinationAddress;

        // Auto-detect from Kestrel urls / 自动检测 Kestrel 绑定地址
        var config = sp.GetRequiredService<IConfiguration>();
        var urls = config["urls"] ?? config["Urls"];
        if (!string.IsNullOrWhiteSpace(urls))
        {
            var first = urls.Split(';')[0].Trim();
            if (first.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return first;
        }

        var env = sp.GetRequiredService<IHostEnvironment>();
        var port = env.IsDevelopment() ? "5001" : "5000";
        return $"http://localhost:{port}";
    }
}
