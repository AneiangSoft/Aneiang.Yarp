using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

/// <summary>Type of entity that changed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiffEntityType
{
    Route,
    Cluster,
    Destination
}
