using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// Session affinity configuration.
/// </summary>
public class SessionAffinityInfo
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("policy")]
    public string? Policy { get; set; }

    [JsonPropertyName("failurePolicy")]
    public string? FailurePolicy { get; set; }

    [JsonPropertyName("affinityKeyName")]
    public string? AffinityKeyName { get; set; }

    [JsonPropertyName("cookie")]
    public SessionAffinityCookieInfo? Cookie { get; set; }
}
