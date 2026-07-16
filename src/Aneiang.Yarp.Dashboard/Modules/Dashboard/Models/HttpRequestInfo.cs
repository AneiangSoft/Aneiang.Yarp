using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// HTTP request configuration.
/// </summary>
public class HttpRequestInfo
{
    [JsonPropertyName("activityTimeout")]
    public string? ActivityTimeout { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("versionPolicy")]
    public string? VersionPolicy { get; set; }

    [JsonPropertyName("allowResponseBuffering")]
    public bool? AllowResponseBuffering { get; set; }
}
