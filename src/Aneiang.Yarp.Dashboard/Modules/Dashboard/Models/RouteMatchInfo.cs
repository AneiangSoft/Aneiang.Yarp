using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

/// <summary>
/// Route match criteria (standard YARP structure).
/// </summary>
public class RouteMatchInfo
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("methods")]
    public IReadOnlyList<string>? Methods { get; set; }

    [JsonPropertyName("hosts")]
    public IReadOnlyList<string>? Hosts { get; set; }

    [JsonPropertyName("headers")]
    public List<RouteHeaderInfo>? Headers { get; set; }

    [JsonPropertyName("queryParameters")]
    public List<RouteQueryParameterInfo>? QueryParameters { get; set; }
}
