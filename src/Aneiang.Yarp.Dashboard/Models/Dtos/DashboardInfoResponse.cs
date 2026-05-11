using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Models.Dtos;

/// <summary>
/// Dashboard basic information response.
/// </summary>
public class DashboardInfoResponse
{
    /// <summary>Product version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Hosting environment name.</summary>
    [JsonPropertyName("environment")]
    public string Environment { get; set; } = string.Empty;

    /// <summary>Application start time.</summary>
    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = string.Empty;

    /// <summary>Application uptime.</summary>
    [JsonPropertyName("uptime")]
    public string Uptime { get; set; } = string.Empty;

    /// <summary>Memory usage in MB.</summary>
    [JsonPropertyName("memoryMb")]
    public double MemoryMb { get; set; }

    /// <summary>Machine name.</summary>
    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = string.Empty;

    /// <summary>Process ID.</summary>
    [JsonPropertyName("processId")]
    public int ProcessId { get; set; }
}
