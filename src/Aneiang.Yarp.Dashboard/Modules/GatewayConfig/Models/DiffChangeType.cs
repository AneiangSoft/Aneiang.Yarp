using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

/// <summary>Type of change.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiffChangeType
{
    Added,
    Removed,
    Modified
}
