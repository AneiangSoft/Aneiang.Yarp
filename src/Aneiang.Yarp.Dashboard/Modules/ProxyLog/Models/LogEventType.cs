using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

/// <summary>
/// Log event type enumeration.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<LogEventType>))]
public enum LogEventType
{
    /// <summary>Incoming proxy request.</summary>
    ProxyRequest,

    /// <summary>Proxy response.</summary>
    ProxyResponse
}
