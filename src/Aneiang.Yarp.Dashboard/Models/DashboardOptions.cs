using Microsoft.AspNetCore.Http;

namespace Aneiang.Yarp.Dashboard.Models;

/// <summary>Dashboard authorization mode / Dashboard 授权模式.</summary>
public enum DashboardAuthMode
{
    /// <summary>No authorization / 无授权.</summary>
    None,

    /// <summary>API key via header or query / API Key 认证.</summary>
    ApiKey,

    /// <summary>JWT with custom username + password / JWT 认证（自定义用户名+密码）.</summary>
    CustomJwt,

    /// <summary>JWT with fixed username "admin" + password / JWT 认证（固定用户名 admin+密码）.</summary>
    DefaultJwt
}

/// <summary>
/// Dashboard options. Binds from <c>Gateway:Dashboard</c> config section.
/// Dashboard 配置选项，绑定自 <c>Gateway:Dashboard</c>.
/// </summary>
public class DashboardOptions
{
    /// <summary>Config section name / 配置节点.</summary>
    public const string SectionName = "Gateway:Dashboard";

    /// <summary>Route prefix for all dashboard pages. Default: "apigateway" / 路由前缀.</summary>
    public string RoutePrefix { get; set; } = "apigateway";

    /// <summary>Auth mode. Default: None / 授权模式.</summary>
    public DashboardAuthMode AuthMode { get; set; } = DashboardAuthMode.None;

    // ─── API Key mode / API Key 模式 ───────────────────────

    /// <summary>API key value. Clients pass via header (default: X-Api-Key) or query param <c>api-key</c> / API 密钥值.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Header name for ApiKey mode. Default: X-Api-Key / API Key 请求头名称.</summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    // ─── JWT mode / JWT 模式 ──────────────────────────────

    /// <summary>JWT signing secret. Auto-generated if not set (invalidated on restart) / JWT 签名密钥.</summary>
    public string? JwtSecret { get; set; }

    /// <summary>Username for CustomJwt mode / CustomJwt 模式的用户名.</summary>
    public string? JwtUsername { get; set; }

    /// <summary>Password for JWT login (required for both CustomJwt and DefaultJwt) / JWT 登录密码.</summary>
    public string? JwtPassword { get; set; }

    // ─── Custom delegate (highest priority) / 自定义委托（最高优先级） ──

    /// <summary>Custom auth delegate. If set, takes precedence over all other auth modes / 自定义认证委托.</summary>
    public Func<HttpContext, Task<bool>>? AuthorizeRequest { get; set; }
}
