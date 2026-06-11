using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Alert.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Models;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Infrastructure.I18n;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Aneiang.Yarp.Dashboard.Infrastructure.Realtime;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Services;
using Aneiang.Yarp.Dashboard.Modules.Policy.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.Alert.Services;
using Microsoft.AspNetCore.Mvc;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Server-Sent Events (SSE) controller for real-time log streaming.
/// Replaces polling-based log updates with low-latency push delivery.
/// </summary>
[Route("api/log-stream")]
public class LogStreamController : ControllerBase
{
    private readonly StructuredLogService? _logService;
    private readonly DashboardJsonContext _jsonContext;

    // Pre-allocated SSE message templates
    private static readonly byte[] SSEHeader = System.Text.Encoding.UTF8.GetBytes("data: ");
    private static readonly byte[] SSETerminator = System.Text.Encoding.UTF8.GetBytes("\n\n");
    private static readonly byte[] SSEKeepAlive = System.Text.Encoding.UTF8.GetBytes(":keepalive\n\n");

    public LogStreamController(
        StructuredLogService? logService,
        DashboardJsonContext jsonContext)
    {
        _logService = logService;
        _jsonContext = jsonContext;
    }

    /// <summary>
    /// Establishes SSE connection for real-time log streaming.
    /// Client receives JSON-encoded LogEntry objects as they are generated.
    /// </summary>
    [HttpGet("logs")]
    [Produces("text/event-stream")]
    public async IAsyncEnumerable<string> StreamLogs(
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_logService == null)
        {
            yield return "data: {\"error\":\"Logging service not available\"}\n\n";
            yield break;
        }

        // Subscribe to log stream
        var channel = _logService.SubscribeToStream();
        var keepAliveTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        try
        {
            // Send initial connection message
            yield return "data: {\"connected\":true}\n\n";

            // Create channel for keepalive coordination
            var keepAliveChannel = Channel.CreateUnbounded<bool>();
            _ = Task.Run(async () =>
            {
                while (await keepAliveTimer.WaitForNextTickAsync(ct))
                {
                    if (!keepAliveChannel.Writer.TryWrite(true))
                        break;
                }
            }, ct);

            var reader = channel.Reader;
            var keepAliveReader = keepAliveChannel.Reader;

            while (!ct.IsCancellationRequested)
            {
                // Wait for either a log entry or keepalive
                var logTask = reader.WaitToReadAsync(ct).AsTask();
                var keepAliveTask = keepAliveReader.WaitToReadAsync(ct).AsTask();

                var completed = await Task.WhenAny(logTask, keepAliveTask);

                if (completed == logTask && await logTask)
                {
                    // Process all available log entries
                    while (reader.TryRead(out var entry))
                    {
                        var json = JsonSerializer.Serialize(entry, _jsonContext.LogEntry);
                        yield return $"data: {json}\n\n";
                    }
                }
                else if (completed == keepAliveTask && await keepAliveTask)
                {
                    // Consume the keepalive signal
                    while (keepAliveReader.TryRead(out _)) { }
                    yield return ":keepalive\n\n";
                }
            }
        }
        finally
        {
            _logService.UnsubscribeFromStream(channel);
            keepAliveTimer.Dispose();
        }
    }

    /// <summary>
    /// Legacy polling endpoint - kept for backward compatibility.
    /// Returns recent logs from the ring buffer.
    /// Consider using /stream/logs for better performance.
    /// </summary>
    [HttpGet("logs/poll")]
    public IActionResult PollLogs([FromServices] IProxyLogStore store, [FromQuery] int count = 100)
    {
        var snapshot = store.GetRecent(count);
        return Ok(new { code = 200, data = snapshot });
    }
}

/// <summary>
/// Minimal wrapper for Channel when System.Threading.Channels is not available.
/// Remove this if you're using System.Threading.Channels package.
/// </summary>
internal static class Channel
{
    public static Channel<T> CreateUnbounded<T>()
    {
        return new Channel<T>();
    }
}

internal class Channel<T>
{
    private readonly System.Collections.Concurrent.BlockingCollection<T> _collection = new();

    public ChannelReader<T> Reader => new ChannelReader<T>(_collection);
    public ChannelWriter<T> Writer => new ChannelWriter<T>(_collection);
}

internal class ChannelReader<T>
{
    private readonly System.Collections.Concurrent.BlockingCollection<T> _collection;

    public ChannelReader(System.Collections.Concurrent.BlockingCollection<T> collection)
    {
        _collection = collection;
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken ct)
    {
        return new ValueTask<bool>(!_collection.IsCompleted);
    }

    public bool TryRead(out T item)
    {
        return _collection.TryTake(out item!);
    }
}

internal class ChannelWriter<T>
{
    private readonly System.Collections.Concurrent.BlockingCollection<T> _collection;

    public ChannelWriter(System.Collections.Concurrent.BlockingCollection<T> collection)
    {
        _collection = collection;
    }

    public bool TryWrite(T item)
    {
        if (_collection.IsAddingCompleted)
            return false;
        return _collection.TryAdd(item);
    }

    public void Complete()
    {
        _collection.CompleteAdding();
    }
}
