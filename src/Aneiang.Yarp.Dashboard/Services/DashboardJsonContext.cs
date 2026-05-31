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
[JsonSerializable(typeof(LogEntry))]
[JsonSerializable(typeof(ProxyLogStoreSnapshot))]
[JsonSerializable(typeof(LogEventType))]
[JsonSerializable(typeof(DashboardRouteResponse))]
[JsonSerializable(typeof(DashboardClusterResponse))]
[JsonSerializable(typeof(DashboardInfoResponse))]
[JsonSerializable(typeof(RouteMatchInfo))]
[JsonSerializable(typeof(RouteHeaderInfo))]
[JsonSerializable(typeof(RouteQueryParameterInfo))]
[JsonSerializable(typeof(RouteDestinationInfo))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<LogEntry>))]
[JsonSerializable(typeof(List<DashboardRouteResponse>))]
[JsonSerializable(typeof(List<DashboardClusterResponse>))]
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
