using Aneiang.Yarp.Dashboard.Infrastructure.Realtime;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// HTTP fallback for the Overview page: serves the same snapshot that the
/// SignalR <c>OverviewUpdate</c> push delivers, so the page stays functional
/// when the realtime connection is unavailable (fallback polling path).
/// </summary>
[Route("api/overview")]
[ApiController]
public sealed class OverviewSnapshotController : ControllerBase
{
    private readonly IOverviewSnapshotProvider _snapshots;

    public OverviewSnapshotController(IOverviewSnapshotProvider snapshots)
    {
        _snapshots = snapshots;
    }

    // GET api/overview/snapshot
    [HttpGet("snapshot")]
    public async Task<IActionResult> GetSnapshot(CancellationToken ct)
    {
        var snapshot = await _snapshots.GetSnapshotAsync(ct);
        return Ok(new { code = 200, data = snapshot });
    }
}
