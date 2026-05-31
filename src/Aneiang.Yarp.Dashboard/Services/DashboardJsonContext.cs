using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Models.Dtos;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// JSON serialization context using System.Text.Json source generators.
/// Provides optimized (de)serialization without reflection for AOT compatibility.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
// Log entries
[JsonSerializable(typeof(LogEntry))]
[JsonSerializable(typeof(ProxyLogStoreSnapshot))]
[JsonSerializable(typeof(LogEventType))]
[JsonSerializable(typeof(List<LogEntry>))]

// Route & Cluster responses
[JsonSerializable(typeof(DashboardRouteResponse))]
[JsonSerializable(typeof(DashboardClusterResponse))]
[JsonSerializable(typeof(DashboardInfoResponse))]
[JsonSerializable(typeof(RouteMatchInfo))]
[JsonSerializable(typeof(RouteHeaderInfo))]
[JsonSerializable(typeof(RouteQueryParameterInfo))]
[JsonSerializable(typeof(RouteDestinationInfo))]
[JsonSerializable(typeof(List<DashboardRouteResponse>))]
[JsonSerializable(typeof(List<DashboardClusterResponse>))]
[JsonSerializable(typeof(List<RouteDestinationInfo>))]
[JsonSerializable(typeof(List<RouteHeaderInfo>))]
[JsonSerializable(typeof(List<RouteQueryParameterInfo>))]

// Collections
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]

// Statistics DTOs
[JsonSerializable(typeof(StatsData))]
[JsonSerializable(typeof(StatusCodeItem))]
[JsonSerializable(typeof(TopItem))]

// Auth & Config
[JsonSerializable(typeof(DashboardLoginResponse))]
[JsonSerializable(typeof(AuthStatus))]
[JsonSerializable(typeof(RateLimitStatus))]
[JsonSerializable(typeof(WebhookSettingsRequest))]

// Generic API response wrappers
[JsonSerializable(typeof(ApiResponse))]
[JsonSerializable(typeof(ApiResponse<>))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(object[]))]
public partial class DashboardJsonContext : JsonSerializerContext
{
    // Static singleton for convenience
    private static DashboardJsonContext? _default;

    /// <summary>
    /// Gets the default serialization context.
    /// </summary>
    public static DashboardJsonContext DefaultContext => _default ??= new DashboardJsonContext(
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
}

/// <summary>Generic API response wrapper.</summary>
public class ApiResponse
{
    public int Code { get; set; }
    public string? Message { get; set; }
}

/// <summary>Generic API response wrapper with data.</summary>
public class ApiResponse<T>
{
    public int Code { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
}

/// <summary>Statistics data DTO.</summary>
public class StatsData
{
    public bool HasData { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public double SuccessRate { get; set; }
    public double ErrorRate { get; set; }
    public double AvgLatency { get; set; }
    public double P50 { get; set; }
    public double P90 { get; set; }
    public double P99 { get; set; }
    public int RequestsPerMin { get; set; }
    public List<StatusCodeItem> StatusCodes { get; set; } = new();
    public List<TopItem> TopRoutes { get; set; } = new();
    public List<TopItem> TopClusters { get; set; } = new();
    public DateTime ComputedAt { get; set; }
}

/// <summary>Status code statistics item.</summary>
public class StatusCodeItem
{
    public int Code { get; set; }
    public int Count { get; set; }
}

/// <summary>Top routes/clusters statistics item.</summary>
public class TopItem
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>Authentication status DTO.</summary>
public class AuthStatus
{
    public bool IsAuthEnabled { get; set; }
    public string AuthMode { get; set; } = string.Empty;
    public string AuthModeDescription { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
}

/// <summary>Rate limit status DTO.</summary>
public class RateLimitStatus
{
    public bool Enabled { get; set; }
    public int PermitLimit { get; set; }
    public int Window { get; set; }
    public int QueueLimit { get; set; }
}
