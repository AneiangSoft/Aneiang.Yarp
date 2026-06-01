using Aneiang.Yarp.Dashboard.Models;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>
/// WAF security event history API.
/// </summary>
[Route("api/security-events")]
[ApiController]
public class SecurityEventsController : ControllerBase
{
    private readonly WafEventStore _eventStore;

    public SecurityEventsController(WafEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    /// <summary>Get recent WAF security events.</summary>
    [HttpGet]
    public IActionResult GetEvents([FromQuery] int count = 100)
    {
        var events = _eventStore.GetRecent(count);
        return Ok(new { code = 200, data = new { entries = events, total = _eventStore.Count } });
    }

    /// <summary>Clear all security event history.</summary>
    [HttpDelete]
    public IActionResult ClearEvents()
    {
        _eventStore.Clear();
        return Ok(new { code = 200, message = "Security events cleared" });
    }

    /// <summary>Get security event summary statistics.</summary>
    [HttpGet("summary")]
    public IActionResult GetEventSummary()
    {
        var events = _eventStore.GetRecent(500);
        var typeCounts = events.GroupBy(e => e.EventType)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
        var topIps = events.GroupBy(e => e.ClientIp)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .ToDictionary(g => g.Key, g => g.Count());

        return Ok(new
        {
            code = 200,
            data = new
            {
                total = _eventStore.Count,
                typeCounts,
                topIps,
                recentCount = events.Count,
                lastEvent = events.FirstOrDefault()?.Timestamp
            }
        });
    }
}
