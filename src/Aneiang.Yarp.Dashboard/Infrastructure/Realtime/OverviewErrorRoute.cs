using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Realtime;

public class OverviewErrorRoute
{
    [JsonPropertyName("routeId")]
    public string RouteId { get; set; } = string.Empty;

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }
}
