using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>API for configuration snapshots and diffs.</summary>
[Route("api/dashboard/config")]
[ApiController]
[Produces("application/json")]
public class ConfigSnapshotController : ControllerBase
{
    private readonly IConfigSnapshotService _snapshotService;

    public ConfigSnapshotController(IConfigSnapshotService snapshotService)
        => _snapshotService = snapshotService;

    /// <summary>Creates a new configuration snapshot.</summary>
    [HttpPost("snapshots")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateSnapshot([FromBody] CreateSnapshotRequest? request)
    {
        var snapshot = await _snapshotService.CreateSnapshotAsync(
            request?.CreatedBy,
            request?.Source,
            request?.Description);

        return Ok(new
        {
            code = 200,
            message = "Snapshot created",
            data = new
            {
                snapshot.Id,
                snapshot.Version,
                snapshot.CreatedAt,
                snapshot.CreatedBy,
                snapshot.Source,
                snapshot.Description,
                routesCount = snapshot.Routes.Count,
                clustersCount = snapshot.Clusters.Count
            }
        });
    }

    /// <summary>Gets all snapshots.</summary>
    [HttpGet("snapshots")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSnapshots([FromQuery] int limit = 50)
    {
        var snapshots = await _snapshotService.GetSnapshotsAsync(limit);
        var data = snapshots.Select(s => new
        {
            s.Id,
            s.Version,
            s.CreatedAt,
            s.CreatedBy,
            s.Source,
            s.Description,
            routesCount = s.Routes.Count,
            clustersCount = s.Clusters.Count
        });
        return Ok(new { code = 200, data });
    }

    /// <summary>Gets a specific snapshot.</summary>
    [HttpGet("snapshots/{id}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSnapshot(string id)
    {
        var snapshot = await _snapshotService.GetSnapshotAsync(id);
        if (snapshot == null)
            return NotFound(new { code = 404, message = $"Snapshot '{id}' not found" });

        return Ok(new { code = 200, data = snapshot });
    }

    /// <summary>Compares two snapshots.</summary>
    [HttpGet("diff")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompareSnapshots(
        [FromQuery] string fromId,
        [FromQuery] string toId = "current")
    {
        if (string.IsNullOrWhiteSpace(fromId))
            return BadRequest(new { code = 400, message = "fromId is required" });

        try
        {
            var result = await _snapshotService.CompareAsync(fromId, toId);
            return Ok(new { code = 200, data = result });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>Compares a snapshot with the current live configuration.</summary>
    [HttpGet("diff/{fromId}/current")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompareWithCurrent(string fromId)
    {
        try
        {
            var result = await _snapshotService.CompareWithCurrentAsync(fromId);
            return Ok(new { code = 200, data = result });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>Deletes old snapshots, keeping only the most recent ones.</summary>
    [HttpDelete("snapshots/cleanup")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> CleanupSnapshots([FromQuery] int keepCount = 100)
    {
        var deleted = await _snapshotService.CleanupOldSnapshotsAsync(keepCount);
        return Ok(new { code = 200, message = $"Would delete {deleted} old snapshots (requires DeleteFromCollectionAsync implementation)" });
    }
}

/// <summary>Request model for creating a snapshot.</summary>
public class CreateSnapshotRequest
{
    public string? CreatedBy { get; set; }
    public string? Source { get; set; }
    public string? Description { get; set; }
}
