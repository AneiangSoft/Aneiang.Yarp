using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Microsoft.AspNetCore.SignalR;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Realtime;

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
        // Clean up route subscriptions for this connection
        foreach (var routeId in _routeSubscriptions.Keys.ToList())
        {
            if (_routeSubscriptions.TryGetValue(routeId, out var connections))
            {
                connections.Remove(Context.ConnectionId);
                // Remove empty entries to prevent memory leak
                if (connections.Count == 0)
                {
                    _routeSubscriptions.TryRemove(routeId, out _);
                }
            }
        }
        await base.OnDisconnectedAsync(exception);
    }
}

