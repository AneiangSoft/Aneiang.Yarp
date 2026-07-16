using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// Session affinity cookie configuration.
/// </summary>
public class SessionAffinityCookieInfo
{
    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("expiration")]
    public string? Expiration { get; set; }

    [JsonPropertyName("maxAge")]
    public string? MaxAge { get; set; }

    [JsonPropertyName("securePolicy")]
    public string? SecurePolicy { get; set; }

    [JsonPropertyName("httpOnly")]
    public bool HttpOnly { get; set; }

    [JsonPropertyName("sameSite")]
    public string? SameSite { get; set; }

    [JsonPropertyName("isEssential")]
    public bool IsEssential { get; set; }
}
