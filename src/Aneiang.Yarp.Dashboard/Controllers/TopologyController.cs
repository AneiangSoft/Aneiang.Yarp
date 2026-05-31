using System.Text.Json.Serialization;
using Aneiang.Yarp.Dashboard.Models.Dtos;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>
/// Provides topology graph data for YARP gateway visualization.
/// </summary>
[ApiController]
[Route("api/topology")]
public class TopologyController : ControllerBase
{
    private readonly IDashboardRouteQueryService _routeQuery;
    private readonly IDashboardClusterQueryService _clusterQuery;

    /// <summary>
    /// Initializes a new instance of TopologyController.
    /// </summary>
    public TopologyController(
        IDashboardRouteQueryService routeQuery,
        IDashboardClusterQueryService clusterQuery)
    {
        _routeQuery = routeQuery;
        _clusterQuery = clusterQuery;
    }

    /// <summary>
    /// Gets the complete topology graph data including routes, clusters, and destinations.
    /// </summary>
    [HttpGet]
    public ActionResult<TopologyGraph> GetTopology()
    {
        var routes = _routeQuery.GetRoutes();
        var clusters = _clusterQuery.GetClusters();

        var graph = new TopologyGraph
        {
            Nodes = BuildNodes(routes, clusters),
            Edges = BuildEdges(routes, clusters),
            Stats = BuildStats(routes, clusters)
        };

        return Ok(graph);
    }

    /// <summary>
    /// Gets traffic flow simulation data for the topology animation.
    /// </summary>
    [HttpGet("traffic-flow")]
    public ActionResult<TrafficFlowData> GetTrafficFlow()
    {
        var routes = _routeQuery.GetRoutes();
        var clusters = _clusterQuery.GetClusters();

        var flow = new TrafficFlowData
        {
            Flows = GenerateTrafficFlows(routes, clusters),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        return Ok(flow);
    }

    private List<TopologyNode> BuildNodes(IReadOnlyList<DashboardRouteResponse> routes, IReadOnlyList<DashboardClusterResponse> clusters)
    {
        var nodes = new List<TopologyNode>();

        // Gateway node (root)
        nodes.Add(new TopologyNode
        {
            Id = "gateway",
            Type = TopologyNodeType.Gateway,
            Label = "YARP Gateway",
            Status = NodeStatus.Healthy,
            Data = new Dictionary<string, object>
            {
                ["routeCount"] = routes.Count,
                ["clusterCount"] = clusters.Count
            }
        });

        // Route nodes
        foreach (var route in routes)
        {
            var status = DetermineRouteStatus(route, clusters);
            nodes.Add(new TopologyNode
            {
                Id = $"route:{route.RouteId}",
                Type = TopologyNodeType.Route,
                Label = route.RouteId,
                Status = status,
                Parent = "gateway",
                Data = new Dictionary<string, object>
                {
                    ["routeId"] = route.RouteId,
                    ["clusterId"] = route.ClusterId,
                    ["path"] = route.Match?.Path,
                    ["methods"] = route.Match?.Methods,
                    ["hosts"] = route.Match?.Hosts,
                    ["order"] = route.Order,
                    ["rateLimiterPolicy"] = route.RateLimiterPolicy,
                    ["timeoutPolicy"] = route.TimeoutPolicy,
                    ["authorizationPolicy"] = route.AuthorizationPolicy,
                    ["transformCount"] = route.Transforms?.Count ?? 0
                }
            });
        }

        // Cluster nodes
        foreach (var cluster in clusters)
        {
            var status = DetermineClusterStatus(cluster);
            nodes.Add(new TopologyNode
            {
                Id = $"cluster:{cluster.ClusterId}",
                Type = TopologyNodeType.Cluster,
                Label = cluster.ClusterId,
                Status = status,
                Data = new Dictionary<string, object>
                {
                    ["clusterId"] = cluster.ClusterId,
                    ["loadBalancingPolicy"] = cluster.LoadBalancingPolicy,
                    ["healthyCount"] = cluster.HealthyCount,
                    ["unhealthyCount"] = cluster.UnhealthyCount,
                    ["unknownCount"] = cluster.UnknownCount,
                    ["totalCount"] = cluster.TotalCount,
                    ["sessionAffinity"] = cluster.SessionAffinity?.Enabled ?? false,
                    ["healthCheckActive"] = cluster.HealthCheck?.Active?.Enabled ?? false,
                    ["healthCheckPassive"] = cluster.HealthCheck?.Passive?.Enabled ?? false
                }
            });
        }

        // Destination nodes
        foreach (var cluster in clusters)
        {
            foreach (var dest in cluster.Destinations)
            {
                var status = DetermineDestinationStatus(dest);
                nodes.Add(new TopologyNode
                {
                    Id = $"dest:{cluster.ClusterId}:{dest.Name}",
                    Type = TopologyNodeType.Destination,
                    Label = dest.Name,
                    Status = status,
                    Parent = $"cluster:{cluster.ClusterId}",
                    Data = new Dictionary<string, object>
                    {
                        ["name"] = dest.Name,
                        ["address"] = dest.Address,
                        ["host"] = dest.Host,
                        ["health"] = dest.Health,
                        ["activeHealth"] = dest.ActiveHealth,
                        ["passiveHealth"] = dest.PassiveHealth,
                        ["clusterId"] = cluster.ClusterId
                    }
                });
            }
        }

        return nodes;
    }

    private List<TopologyEdge> BuildEdges(IReadOnlyList<DashboardRouteResponse> routes, IReadOnlyList<DashboardClusterResponse> clusters)
    {
        var edges = new List<TopologyEdge>();

        // Gateway -> Route connections
        foreach (var route in routes)
        {
            edges.Add(new TopologyEdge
            {
                Id = $"edge:gateway:{route.RouteId}",
                Source = "gateway",
                Target = $"route:{route.RouteId}",
                Type = EdgeType.Request,
                Label = route.Match?.Path
            });
        }

        // Route -> Cluster connections (if route has clusterId)
        foreach (var route in routes.Where(r => !string.IsNullOrEmpty(r.ClusterId)))
        {
            edges.Add(new TopologyEdge
            {
                Id = $"edge:{route.RouteId}:{route.ClusterId}",
                Source = $"route:{route.RouteId}",
                Target = $"cluster:{route.ClusterId}",
                Type = EdgeType.Forward,
                Label = route.ClusterId
            });
        }

        // Cluster -> Destination connections (already implied by parent-child in node structure)
        foreach (var cluster in clusters)
        {
            foreach (var dest in cluster.Destinations)
            {
                edges.Add(new TopologyEdge
                {
                    Id = $"edge:{cluster.ClusterId}:{dest.Name}",
                    Source = $"cluster:{cluster.ClusterId}",
                    Target = $"dest:{cluster.ClusterId}:{dest.Name}",
                    Type = EdgeType.Proxy,
                    Label = ""
                });
            }
        }

        return edges;
    }

    private TopologyStats BuildStats(IReadOnlyList<DashboardRouteResponse> routes, IReadOnlyList<DashboardClusterResponse> clusters)
    {
        var healthyDestinations = clusters.Sum(c => c.HealthyCount);
        var unhealthyDestinations = clusters.Sum(c => c.UnhealthyCount);
        var totalDestinations = clusters.Sum(c => c.TotalCount);

        return new TopologyStats
        {
            RouteCount = routes.Count,
            ClusterCount = clusters.Count,
            DestinationCount = totalDestinations,
            HealthyCount = healthyDestinations,
            UnhealthyCount = unhealthyDestinations,
            UnlinkedRoutes = routes.Count(r => string.IsNullOrEmpty(r.ClusterId)),
            UnlinkedClusters = clusters.Count(c => !routes.Any(r => r.ClusterId == c.ClusterId))
        };
    }

    private List<TrafficFlow> GenerateTrafficFlows(IReadOnlyList<DashboardRouteResponse> routes, IReadOnlyList<DashboardClusterResponse> clusters)
    {
        var flows = new List<TrafficFlow>();
        var random = new Random();

        // Simulate traffic from gateway to routes
        foreach (var route in routes)
        {
            if (!string.IsNullOrEmpty(route.ClusterId))
            {
                flows.Add(new TrafficFlow
                {
                    From = "gateway",
                    To = $"route:{route.RouteId}",
                    Intensity = random.Next(10, 100) / 100.0,
                    Status = random.Next(10) > 2 ? FlowStatus.Normal : FlowStatus.Error
                });

                flows.Add(new TrafficFlow
                {
                    From = $"route:{route.RouteId}",
                    To = $"cluster:{route.ClusterId}",
                    Intensity = random.Next(5, 90) / 100.0,
                    Status = random.Next(10) > 2 ? FlowStatus.Normal : FlowStatus.Error
                });
            }
        }

        return flows;
    }

    private static NodeStatus DetermineRouteStatus(DashboardRouteResponse route, IReadOnlyList<DashboardClusterResponse> clusters)
    {
        if (string.IsNullOrEmpty(route.ClusterId))
            return NodeStatus.Warning; // Orphan route

        var cluster = clusters.FirstOrDefault(c => c.ClusterId == route.ClusterId);
        if (cluster == null)
            return NodeStatus.Warning; // Linked to non-existent cluster

        return cluster.UnhealthyCount == cluster.TotalCount ? NodeStatus.Error : NodeStatus.Healthy;
    }

    private static NodeStatus DetermineClusterStatus(DashboardClusterResponse cluster)
    {
        if (cluster.UnhealthyCount > 0 && cluster.HealthyCount == 0)
            return NodeStatus.Error;
        if (cluster.UnhealthyCount > 0)
            return NodeStatus.Warning;
        return NodeStatus.Healthy;
    }

    private static NodeStatus DetermineDestinationStatus(DashboardDestinationResponse dest)
    {
        if (dest.Health == "Healthy" || dest.ActiveHealth == "Healthy" || dest.PassiveHealth == "Healthy")
            return NodeStatus.Healthy;
        if (dest.Health == "Unhealthy" || dest.ActiveHealth == "Unhealthy" || dest.PassiveHealth == "Unhealthy")
            return NodeStatus.Error;
        return NodeStatus.Unknown;
    }
}

/// <summary>
/// Represents a complete topology graph.
/// </summary>
public class TopologyGraph
{
    [JsonPropertyName("nodes")]
    public List<TopologyNode> Nodes { get; set; } = new();

    [JsonPropertyName("edges")]
    public List<TopologyEdge> Edges { get; set; } = new();

    [JsonPropertyName("stats")]
    public TopologyStats Stats { get; set; } = new();
}

/// <summary>
/// Represents a node in the topology graph.
/// </summary>
public class TopologyNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public TopologyNodeType Type { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public NodeStatus Status { get; set; }

    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    [JsonPropertyName("data")]
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// Represents an edge in the topology graph.
/// </summary>
public class TopologyEdge
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public EdgeType Type { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }
}

/// <summary>
/// Statistics for the topology graph.
/// </summary>
public class TopologyStats
{
    [JsonPropertyName("routeCount")]
    public int RouteCount { get; set; }

    [JsonPropertyName("clusterCount")]
    public int ClusterCount { get; set; }

    [JsonPropertyName("destinationCount")]
    public int DestinationCount { get; set; }

    [JsonPropertyName("healthyCount")]
    public int HealthyCount { get; set; }

    [JsonPropertyName("unhealthyCount")]
    public int UnhealthyCount { get; set; }

    [JsonPropertyName("unlinkedRoutes")]
    public int UnlinkedRoutes { get; set; }

    [JsonPropertyName("unlinkedClusters")]
    public int UnlinkedClusters { get; set; }
}

/// <summary>
/// Traffic flow simulation data.
/// </summary>
public class TrafficFlowData
{
    [JsonPropertyName("flows")]
    public List<TrafficFlow> Flows { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}

/// <summary>
/// Individual traffic flow.
/// </summary>
public class TrafficFlow
{
    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;

    [JsonPropertyName("intensity")]
    public double Intensity { get; set; }

    [JsonPropertyName("status")]
    public FlowStatus Status { get; set; }
}

/// <summary>
/// Types of topology nodes.
/// </summary>
public enum TopologyNodeType
{
    Gateway,
    Route,
    Cluster,
    Destination
}

/// <summary>
/// Node health status.
/// </summary>
public enum NodeStatus
{
    Healthy,
    Warning,
    Error,
    Unknown
}

/// <summary>
/// Types of topology edges.
/// </summary>
public enum EdgeType
{
    Request,
    Forward,
    Proxy
}

/// <summary>
/// Traffic flow status.
/// </summary>
public enum FlowStatus
{
    Normal,
    Error,
    Warning
}
