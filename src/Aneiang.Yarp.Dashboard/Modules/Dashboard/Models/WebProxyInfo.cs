using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// Web proxy configuration.
/// </summary>
public class WebProxyInfo
{
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("bypassOnLocal")]
    public bool BypassOnLocal { get; set; }

    [JsonPropertyName("useDefaultCredentials")]
    public bool UseDefaultCredentials { get; set; }
}
