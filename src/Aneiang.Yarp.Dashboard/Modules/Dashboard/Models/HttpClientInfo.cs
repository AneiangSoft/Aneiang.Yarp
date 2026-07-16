using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// HTTP client configuration.
/// </summary>
public class HttpClientInfo
{
    [JsonPropertyName("sslProtocols")]
    public string? SslProtocols { get; set; }

    [JsonPropertyName("dangerousAcceptAnyServerCertificate")]
    public bool DangerousAcceptAnyServerCertificate { get; set; }

    [JsonPropertyName("maxConnectionsPerServer")]
    public int? MaxConnectionsPerServer { get; set; }

    [JsonPropertyName("enableMultipleHttp2Connections")]
    public bool EnableMultipleHttp2Connections { get; set; }

    [JsonPropertyName("requestHeaderEncoding")]
    public string? RequestHeaderEncoding { get; set; }

    [JsonPropertyName("responseHeaderEncoding")]
    public string? ResponseHeaderEncoding { get; set; }

    [JsonPropertyName("webProxy")]
    public WebProxyInfo? WebProxy { get; set; }
}
