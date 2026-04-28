namespace Aneiang.Yarp.Models;

/// <summary>Gateway API auth mode / 网关 API 认证模式.</summary>
public enum GatewayApiAuthMode
{
    /// <summary>No authentication / 无认证.</summary>
    None = 0,

    /// <summary>HTTP Basic Authentication / HTTP Basic 认证.</summary>
    BasicAuth,

    /// <summary>API Key via X-Api-Key header or query / API Key 认证.</summary>
    ApiKey,
}

/// <summary>
/// Options for GatewayConfigController API auth. Binds from <c>Gateway:ApiAuth</c>.
/// 网关配置 API 授权选项，绑定自 <c>Gateway:ApiAuth</c>.
/// </summary>
public class GatewayApiAuthOptions
{
    /// <summary>Config section name / 配置节点.</summary>
    public const string SectionName = "Gateway:ApiAuth";

    /// <summary>Auth mode. Default: None / 认证模式.</summary>
    public GatewayApiAuthMode Mode { get; set; } = GatewayApiAuthMode.None;

    /// <summary>Username for BasicAuth / Basic 认证用户名.</summary>
    public string? Username { get; set; }

    /// <summary>Password for BasicAuth / Basic 认证密码.</summary>
    public string? Password { get; set; }

    /// <summary>API Key for ApiKey mode / API 密钥.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Header name for ApiKey mode. Default: X-Api-Key / API Key 请求头名称.</summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";
}
