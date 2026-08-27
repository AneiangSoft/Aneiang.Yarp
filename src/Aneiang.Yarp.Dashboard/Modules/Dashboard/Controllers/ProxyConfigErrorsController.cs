using Aneiang.Yarp.Dashboard.Infrastructure.Realtime;
using Aneiang.Yarp.Services.ProxyConfigHealth;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Read-only access to captured proxy configuration apply errors. Consumed by the
/// overview banner (health summary) and by the cluster/route list pages (per-row
/// markers via <see cref="ProxyConfigError.RouteId"/>/<see cref="ProxyConfigError.ClusterId"/>).
/// </summary>
[Route("api/config")]
[ApiController]
public sealed class ProxyConfigErrorsController : ControllerBase
{
    private readonly IProxyConfigErrorStore _errorStore;
    private readonly IOverviewSnapshotProvider _snapshotProvider;

    public ProxyConfigErrorsController(
        IProxyConfigErrorStore errorStore,
        IOverviewSnapshotProvider snapshotProvider)
    {
        _errorStore = errorStore;
        _snapshotProvider = snapshotProvider;
    }

    // GET api/config/apply-errors?since=ISO8601&scope=cluster|route
    /// <summary>
    /// Returns the current proxy config health summary plus the recent apply errors,
    /// optionally filtered since a timestamp and by scope (cluster/route).
    /// </summary>
    /// <param name="since">Optional ISO-8601 UTC timestamp; only errors at or after it are returned.</param>
    /// <param name="scope">Optional <c>cluster</c> or <c>route</c> to filter attributed errors.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("apply-errors")]
    public async Task<IActionResult> GetApplyErrors(
        [FromQuery] string? since,
        [FromQuery] string? scope,
        CancellationToken ct)
    {
        DateTime? sinceUtc = null;
        if (!string.IsNullOrWhiteSpace(since)
            && DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            sinceUtc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }

        var normalizedScope = string.IsNullOrWhiteSpace(scope) ? null : scope;

        var errors = _errorStore.GetErrors(sinceUtc, normalizedScope);
        var health = _errorStore.GetHealth();

        // Refresh health from the shared snapshot provider so the banner stays in sync
        // with the 5s SignalR push even when the realtime connection is down.
        ProxyConfigHealthSnapshot? snapshotHealth = null;
        try
        {
            var snapshot = await _snapshotProvider.GetSnapshotAsync(ct);
            snapshotHealth = snapshot.ProxyConfigHealth;
        }
        catch (Exception)
        {
            // Fall back to the store's own health snapshot.
        }

        return Ok(new
        {
            code = 200,
            data = new
            {
                health = snapshotHealth ?? health,
                errors
            }
        });
    }
}
