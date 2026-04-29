namespace Aneiang.Yarp.Models;

/// <summary>Gateway API authentication mode.</summary>
public enum GatewayApiAuthMode
{
    /// <summary>No authentication.</summary>
    None = 0,

    /// <summary>HTTP Basic Authentication.</summary>
    BasicAuth,

    /// <summary>API Key via X-Api-Key header or query parameter.</summary>
    ApiKey,
}

/// <summary>
/// Options for GatewayConfigController API authentication. Binds from <c>Gateway:ApiAuth</c>.
/// </summary>
public class GatewayApiAuthOptions
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Gateway:ApiAuth";

    /// <summary>Authentication mode. Default: None.</summary>
    public GatewayApiAuthMode Mode { get; set; } = GatewayApiAuthMode.None;

    /// <summary>Username for BasicAuth.</summary>
    public string? Username { get; set; }

    /// <summary>Password for BasicAuth.</summary>
    public string? Password { get; set; }

    /// <summary>API Key for ApiKey mode.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Header name for ApiKey mode. Default: X-Api-Key.</summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";
}
