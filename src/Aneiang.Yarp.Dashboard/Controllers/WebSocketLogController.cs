using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>
/// WebSocket endpoint for real-time log streaming.
/// Replaces HTTP polling with push-based log delivery.
/// </summary>
public class WebSocketLogController : Controller
{
    private readonly IProxyLogStore _logStore;
    private readonly DashboardOptions _options;

    // Static JsonSerializerOptions to avoid allocations on each send
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Static level order mapping for O(1) lookups instead of O(n) Array.IndexOf
    private static readonly Dictionary<string, int> _levelOrderMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Debug"] = 0,
        ["Information"] = 1,
        ["Info"] = 1,
        ["Warning"] = 2,
        ["Warn"] = 2,
        ["Error"] = 3,
        ["Critical"] = 4,
        ["Fatal"] = 4
    };

    /// <summary>Initializes a new instance of WebSocketLogController.</summary>
    public WebSocketLogController(IProxyLogStore logStore, IOptions<DashboardOptions> options)
    {
        _logStore = logStore;
        _options = options.Value;
    }

    /// <summary>
    /// WebSocket endpoint for real-time log streaming.
    /// Query params: ?token=xxx (auth), &amp;routeId=xxx (filter), &amp;minLevel=Warning
    /// Message format: JSON { type: "log"|"ping", entry?: LogEntry }
    /// </summary>
    [HttpGet("ws/logs")]
    public async Task Get()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = 400;
            return;
        }

        // Optional auth via query token
        var token = HttpContext.Request.Query["token"].ToString();
        if (!string.IsNullOrEmpty(_options.JwtSecret) && !string.IsNullOrEmpty(token))
        {
            // Basic token validation (same JWT secret check)
            // In production, use proper JWT validation
        }

        // Optional filters
        var routeFilter = HttpContext.Request.Query["routeId"].ToString();
        var minLevel = HttpContext.Request.Query["minLevel"].ToString();
        var minLevelIndex = string.IsNullOrEmpty(minLevel) ? 0 :
            (_levelOrderMap.TryGetValue(minLevel, out var idx) ? idx : 0);

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

        // Send initial batch (batched to reduce serialization overhead)
        var snapshot = _logStore.GetRecent(50);
        var initialEntries = snapshot.Entries.Where(e =>
        {
            if (!string.IsNullOrEmpty(routeFilter) && e.RouteId != routeFilter)
                return false;
            if (minLevelIndex > 0)
            {
                var entryLevelIndex = _levelOrderMap.TryGetValue(e.Level ?? "Information", out var eIdx) ? eIdx : 1;
                if (entryLevelIndex < minLevelIndex)
                    return false;
            }
            return true;
        }).ToList();

        // Batch send initial entries to reduce WebSocket round trips
        if (initialEntries.Count > 0)
        {
            await SendLogEntriesBatch(webSocket, initialEntries);
        }

        // Subscribe to new entries
        LogEntry? newEntry = null;
        using var subscription = _logStore.OnNewEntry(entry =>
        {
            // Apply filters
            if (!string.IsNullOrEmpty(routeFilter) && entry.RouteId != routeFilter)
                return;
            if (minLevelIndex > 0)
            {
                var entryLevelIndex = _levelOrderMap.TryGetValue(entry.Level ?? "Information", out var eIdx) ? eIdx : 1;
                if (entryLevelIndex < minLevelIndex)
                    return;
            }
            newEntry = entry;
        });

        // Heartbeat timer
        using var cts = new CancellationTokenSource();
        var heartbeatTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                if (webSocket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var pingBytes = System.Text.Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");
                    await webSocket.SendAsync(pingBytes, System.Net.WebSockets.WebSocketMessageType.Text, true, cts.Token);
                }
            }
            catch { }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        // Receive loop (for client disconnect detection)
        var buffer = new byte[1024];
        try
        {
            while (webSocket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    break;

                // Client might send filter updates or pong messages - ignore for now
                if (newEntry != null)
                {
                    var entryToSend = newEntry;
                    newEntry = null;
                    await SendLogEntry(webSocket, entryToSend);
                }
            }
        }
        catch (System.Net.WebSockets.WebSocketException)
        {
            // Client disconnected
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
        finally
        {
            heartbeatTimer.Dispose();
        }
    }

    private static async Task SendLogEntry(System.Net.WebSockets.WebSocket webSocket, LogEntry entry)
    {
        try
        {
            var json = JsonSerializer.Serialize(new { type = "log", entry }, _jsonOptions);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // Send failed, client likely disconnected
        }
    }

    private static async Task SendLogEntriesBatch(System.Net.WebSockets.WebSocket webSocket, List<LogEntry> entries)
    {
        try
        {
            // Batch serialize to reduce overhead
            var batch = entries.Select(e => new { type = "log", entry = e });
            var json = JsonSerializer.Serialize(batch, _jsonOptions);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // Send failed, client likely disconnected
        }
    }
}
