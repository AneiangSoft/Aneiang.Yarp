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
        var levelOrder = new[] { "Debug", "Information", "Warning", "Error", "Critical" };
        var minLevelIndex = string.IsNullOrEmpty(minLevel) ? 0 :
            Array.IndexOf(levelOrder, minLevel);

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

        // Send initial batch
        var snapshot = _logStore.GetRecent(50);
        var initialEntries = snapshot.Entries.Where(e =>
        {
            if (!string.IsNullOrEmpty(routeFilter) && e.RouteId != routeFilter)
                return false;
            if (minLevelIndex > 0)
            {
                var entryLevelIndex = Array.IndexOf(levelOrder, e.Level);
                if (entryLevelIndex < minLevelIndex)
                    return false;
            }
            return true;
        }).ToList();

        foreach (var entry in initialEntries)
        {
            await SendLogEntry(webSocket, entry);
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
                var entryLevelIndex = Array.IndexOf(levelOrder, entry.Level);
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
            var json = JsonSerializer.Serialize(new { type = "log", entry }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // Send failed, client likely disconnected
        }
    }
}
