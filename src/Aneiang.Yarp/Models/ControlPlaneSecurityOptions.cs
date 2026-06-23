namespace Aneiang.Yarp.Models;

/// <summary>
/// Unified control-plane security options for Gateway REST API, gRPC registration, and Dashboard.
/// Binds from <c>Gateway:Security:ControlPlane</c>.
/// </summary>
public class ControlPlaneSecurityOptions
{
    public const string SectionName = "Gateway:Security:ControlPlane";

    /// <summary>Auth mode: None, ApiKey, BasicAuth, DefaultJwt, CustomJwt.</summary>
    public string? AuthMode { get; set; }

    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ApiKey { get; set; }
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>Allow API key from query string. Disabled by default.</summary>
    public bool AllowApiKeyInQuery { get; set; } = false;

    /// <summary>Fail startup in Production when control-plane auth is disabled.</summary>
    public bool RequireAuthInProduction { get; set; } = true;
}
