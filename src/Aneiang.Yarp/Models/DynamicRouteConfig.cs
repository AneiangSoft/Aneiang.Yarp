using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Models;

public sealed class DynamicRouteConfig
{
    public RouteConfig Config { get; set; } = new() { RouteId = string.Empty, ClusterId = string.Empty };

    public string RouteUid { get; set; } = Guid.NewGuid().ToString("N");

    public string? DisplayName { get; set; }

    public string? ClusterUid { get; set; }

    public string Source { get; set; } = "dynamic";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string? CreatedBy { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new();
}
