using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.SignalR;

namespace Aneiang.Yarp.Dashboard.Hubs;

/// <summary>
/// SignalR hub for real-time traffic flow visualization on the topology page.
/// Clients subscribe to receive live traffic intensity updates per route/cluster edge.
/// </summary>
public class TrafficHub : Hub
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> _routeSubscriptions = new();
    private readonly IDashboardAuthorizationService _authorizationService;

    public TrafficHub(IDashboardAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    public async Task SubscribeToRoutes(string[] routeIds)
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext == null || !await _authorizationService.IsAuthorizedAsync(httpContext))
        {
            Context.Abort();
            return;
        }

        var groupName = string.Join(",", routeIds.OrderBy(r => r));
        foreach (var routeId in routeIds)
        {
            _routeSubscriptions.AddOrUpdate(
                routeId,
                _ => new HashSet<string> { Context.ConnectionId },
                (_, set) => { set.Add(Context.ConnectionId); return set; });
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var (routeId, connections) in _routeSubscriptions)
        {
            connections.Remove(Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Real-time traffic data for a single route/cluster edge.
/// Broadcast to SignalR clients for live topology animation.
/// </summary>
public class RealTimeTrafficData
{
    [JsonPropertyName("routeId")]
    public string RouteId { get; set; } = string.Empty;

    [JsonPropertyName("clusterId")]
    public string? ClusterId { get; set; }

    [JsonPropertyName("requestsPerSecond")]
    public double RequestsPerSecond { get; set; }

    [JsonPropertyName("requestsPerMinute")]
    public int RequestsPerMinute { get; set; }

    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; }

    [JsonPropertyName("avgLatencyMs")]
    public double AvgLatencyMs { get; set; }

    [JsonPropertyName("p99LatencyMs")]
    public double P99LatencyMs { get; set; }

    [JsonPropertyName("bytesIn")]
    public long BytesIn { get; set; }

    [JsonPropertyName("bytesOut")]
    public long BytesOut { get; set; }

    [JsonPropertyName("activeConnections")]
    public int ActiveConnections { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "normal";

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}
