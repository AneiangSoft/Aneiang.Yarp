using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

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

    /// <summary>CPU usage percentage (0-100).</summary>
    [JsonPropertyName("cpuUsage")]
    public double CpuUsage { get; set; }

    /// <summary>Total memory in bytes (for memory bar percentage calculation).</summary>
    [JsonPropertyName("totalMemory")]
    public long TotalMemory { get; set; }

    /// <summary>Memory working set in bytes.</summary>
    [JsonPropertyName("memoryWorkingSet")]
    public long MemoryWorkingSet { get; set; }

    /// <summary>Total GC count since process start.</summary>
    [JsonPropertyName("gcCount")]
    public int GcCount { get; set; }

    /// <summary>Current thread count.</summary>
    [JsonPropertyName("threadCount")]
    public int ThreadCount { get; set; }
}
