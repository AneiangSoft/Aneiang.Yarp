using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Microsoft.AspNetCore.SignalR;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Realtime;

/// <summary>
/// SignalR hub for Overview page real-time push.
/// Clients are auto-added to the "overview" group on connect and receive
/// aggregated stat-card, system-health, and top-issues updates every 5 seconds.
/// No client-invokable methods; the hub is pure server-push.
/// </summary>
public class OverviewHub : Hub
{
    private readonly IDashboardAuthorizationService _authorizationService;

    /// <summary>
    /// Initializes a new instance of <see cref="OverviewHub"/>.
    /// </summary>
    public OverviewHub(IDashboardAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext == null || !await _authorizationService.IsAuthorizedAsync(httpContext))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, "overview");
        await base.OnConnectedAsync();
    }
}
