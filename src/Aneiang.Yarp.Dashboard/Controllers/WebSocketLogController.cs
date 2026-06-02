using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>
/// WebSocket endpoint for real-time log streaming.
/// Replaces HTTP polling with push-based log delivery.
/// Optimized with static pooled buffers and cached values.
/// </summary>
public class WebSocketLogController : Controller
{
    private readonly IProxyLogStore _logStore;
    private readonly string? _jwtSecret;
    private readonly bool _hasAuth;
    private readonly ILogger<WebSocketLogController> _logger;

    // Use source generator context for optimized serialization (replaces JsonSerializerOptions)
    private static readonly DashboardJsonContext _jsonContext = DashboardJsonContext.DefaultContext;

    // Static pre-encoded ping message to avoid repeated UTF8 encoding
    private static readonly byte[] _pingMessage = System.Text.Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");

    // Static level order mapping for O(1) lookups instead of O(n) Array.IndexOf
    private static readonly Dictionary<string, int> _levelOrderMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Debug"] = 0,
        ["Verbose"] = 0,
        ["Trace"] = 0,
        ["Information"] = 1,
        ["Info"] = 1,
        ["Warning"] = 2,
        ["Warn"] = 2,
        ["Error"] = 3,
        ["Critical"] = 4,
        ["Fatal"] = 4
    };

    /// <summary>Initializes a new instance of WebSocketLogController.</summary>
    public WebSocketLogController(IProxyLogStore logStore, IOptions<DashboardOptions> options, ILogger<WebSocketLogController> logger)
    {
        _logStore = logStore;
        _jwtSecret = options.Value.JwtSecret;
        _hasAuth = !string.IsNullOrEmpty(_jwtSecret);
        _logger = logger;
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

        // Optional auth via query token (only check if auth is configured)
        if (_hasAuth)
        {
            var token = HttpContext.Request.Query["token"].ToString();
            // Basic token validation placeholder
            // In production, use proper JWT validation
        }

        // Optional filters - read once and cache locally
        var routeFilter = HttpContext.Request.Query["routeId"].ToString();
        var minLevel = HttpContext.Request.Query["minLevel"].ToString();
        var minLevelIndex = string.IsNullOrEmpty(minLevel) ? 0 :
            (_levelOrderMap.TryGetValue(minLevel, out var idx) ? idx : 0);

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        using var cts = new CancellationTokenSource();

        // Send initial batch (batched to reduce serialization overhead)
        var snapshot = _logStore.GetRecent(50);
        var initialEntries = FilterEntries(snapshot.Entries, routeFilter, minLevelIndex);

        // Batch send initial entries to reduce WebSocket round trips
        if (initialEntries.Count > 0)
        {
            await SendLogEntriesBatch(webSocket, initialEntries);
        }

        // Subscribe to new entries
        LogEntry? newEntry = null;
        using var subscription = _logStore.OnNewEntry(entry =>
        {
            // Apply filters inline for performance
            if (!string.IsNullOrEmpty(routeFilter) && entry.RouteId != routeFilter)
                return;
            if (minLevelIndex > 0 && GetLevelIndex(entry.Level) < minLevelIndex)
                return;
            newEntry = entry;
        });

        // Start heartbeat using async loop instead of Timer to reduce handle overhead
        var heartbeatTask = RunHeartbeatAsync(webSocket, cts.Token);

        // Receive loop (for client disconnect detection)
        var buffer = ArrayPool<byte>.Shared.Rent(1024);
        try
        {
            while (webSocket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    break;

                // Send any pending new entry
                if (newEntry != null)
                {
                    var entryToSend = newEntry;
                    newEntry = null;
                    await SendLogEntryAsync(webSocket, entryToSend);
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
            ArrayPool<byte>.Shared.Return(buffer);
            cts.Cancel();
            try { await heartbeatTask; }
            catch (OperationCanceledException) { /* Expected on shutdown */ }
            catch (Exception ex) { _logger.LogDebug(ex, "Heartbeat task ended unexpectedly"); }
        }
    }

    /// <summary>
    /// Runs heartbeat using async loop instead of Timer to reduce resource overhead.
    /// </summary>
    private static async Task RunHeartbeatAsync(System.Net.WebSockets.WebSocket webSocket, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && webSocket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                if (webSocket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    // Use static pre-encoded ping message
                    await webSocket.SendAsync(
                        _pingMessage,
                        System.Net.WebSockets.WebSocketMessageType.Text,
                        true,
                        ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (System.Net.WebSockets.WebSocketException)
        {
            // Client disconnected
        }
        catch (Exception)
        {
            // Ignore other errors (network issues, etc.)
        }
    }

    /// <summary>
    /// Gets the level index, defaulting to Information (1) for unknown levels.
    /// </summary>
    private static int GetLevelIndex(string? level)
    {
        if (string.IsNullOrEmpty(level))
            return 1;
        return _levelOrderMap.TryGetValue(level, out var idx) ? idx : 1;
    }

    /// <summary>
    /// Filters entries based on route and level criteria.
    /// </summary>
    private static List<LogEntry> FilterEntries(List<LogEntry> entries, string routeFilter, int minLevelIndex)
    {
        if (string.IsNullOrEmpty(routeFilter) && minLevelIndex <= 0)
            return entries;

        var result = new List<LogEntry>(entries.Count);
        foreach (var e in entries)
        {
            if (!string.IsNullOrEmpty(routeFilter) && e.RouteId != routeFilter)
                continue;
            if (minLevelIndex > 0 && GetLevelIndex(e.Level) < minLevelIndex)
                continue;
            result.Add(e);
        }
        return result;
    }

    // Anonymous type wrapper for log messages
    private readonly record struct LogMessage(string Type, LogEntry Entry);

    /// <summary>
    /// Sends a single log entry with exception handling.
    /// Uses source generator for optimal serialization performance.
    /// </summary>
    private static async Task SendLogEntryAsync(System.Net.WebSockets.WebSocket webSocket, LogEntry entry)
    {
        try
        {
            // Use source generator for faster serialization
            var json = JsonSerializer.Serialize(entry, _jsonContext.LogEntry);
            var wrapped = $"{{\"type\":\"log\",\"entry\":{json}}}";
            var bytes = System.Text.Encoding.UTF8.GetBytes(wrapped);
            await webSocket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // Send failed, client likely disconnected
        }
    }

    /// <summary>
    /// Sends batch of log entries with reduced serialization overhead.
    /// Uses source generator for optimal performance.
    /// </summary>
    private static async Task SendLogEntriesBatch(System.Net.WebSockets.WebSocket webSocket, List<LogEntry> entries)
    {
        try
        {
            // Build JSON array manually for batch to avoid anonymous type overhead
            var sb = new System.Text.StringBuilder(entries.Count * 256);
            sb.Append('[');
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var entryJson = JsonSerializer.Serialize(entries[i], _jsonContext.LogEntry);
                sb.Append("{\"type\":\"log\",\"entry\":");
                sb.Append(entryJson);
                sb.Append('}');
            }
            sb.Append(']');

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            await webSocket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // Send failed, client likely disconnected
        }
    }
}
