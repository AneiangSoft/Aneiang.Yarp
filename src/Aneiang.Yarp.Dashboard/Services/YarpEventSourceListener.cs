using System.Diagnostics.Tracing;
using Aneiang.Yarp.Dashboard.Models;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Listens to YARP's internal EventSource events and writes them to the ProxyLogStore.
/// This works regardless of the logging framework (Serilog, NLog, etc.) being used.
/// 监听 YARP 内部 EventSource 事件，无论使用何种日志框架都能捕获日志.
/// </summary>
public sealed class YarpEventSourceListener : EventListener
{
    private const string YarpEventSourceName = "Yarp.ReverseProxy";
    private readonly ProxyLogStore _store;
    private EventSource? _yarpEventSource;

    /// <summary>
    /// Creates a new listener linked to the specified store.
    /// </summary>
    public YarpEventSourceListener(ProxyLogStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Called when a new EventSource is created. We check if it's YARP's EventSource.
    /// </summary>
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        base.OnEventSourceCreated(eventSource);

        if (eventSource.Name == YarpEventSourceName || 
            eventSource.Name.StartsWith(YarpEventSourceName + ".", StringComparison.Ordinal))
        {
            _yarpEventSource = eventSource;

            // Enable all events from YARP EventSource
            // EventLevel.Verbose (5) captures everything, we'll filter later
            EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
        }
    }

    /// <summary>
    /// Called when an event is written. We parse YARP events and store them.
    /// </summary>
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (_yarpEventSource == null) return;

        try
        {
            // Only process YARP events
            if (eventData.EventSource.Name != _yarpEventSource.Name) return;

            // Extract event data
            var level = MapEventLevelToLogLevel(eventData.Level);
            
            // Filter: only capture Information and above (same as ILoggerProvider)
            if (level < Microsoft.Extensions.Logging.LogLevel.Information) return;

            var message = FormatEventMessage(eventData);
            if (string.IsNullOrEmpty(message)) return;

            _store.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level.ToString(),
                Category = eventData.EventSource.Name,
                Message = message,
                Exception = null // EventSource events typically don't include exceptions
            });
        }
        catch
        {
            // Silently ignore errors in event processing to avoid recursion
        }
    }

    /// <summary>
    /// Maps EventLevel to Microsoft.Extensions.Logging.LogLevel.
    /// </summary>
    private static Microsoft.Extensions.Logging.LogLevel MapEventLevelToLogLevel(EventLevel level)
    {
        return level switch
        {
            EventLevel.Critical => Microsoft.Extensions.Logging.LogLevel.Critical,
            EventLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            EventLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            EventLevel.Informational => Microsoft.Extensions.Logging.LogLevel.Information,
            EventLevel.Verbose => Microsoft.Extensions.Logging.LogLevel.Debug,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };
    }

    /// <summary>
    /// Formats an EventWrittenEventArgs into a readable message.
    /// </summary>
    private static string FormatEventMessage(EventWrittenEventArgs eventData)
    {
        if (eventData.Payload == null || eventData.Payload.Count == 0)
            return eventData.EventName ?? "Unknown";

        var eventName = eventData.EventName ?? "Unknown";
        
        // Special formatting for common YARP events
        return eventName switch
        {
            "ForwarderStart" => FormatForwarderStart(eventData),
            "ForwarderStop" => FormatForwarderStop(eventData),
            "ForwarderInvoke" => FormatForwarderInvoke(eventData),
            "ForwarderStage" => FormatForwarderStage(eventData),
            "ContentTransferred" => FormatContentTransferred(eventData),
            _ => FormatDefaultEvent(eventData)
        };
    }

    private static string FormatForwarderStart(EventWrittenEventArgs eventData)
    {
        var destPrefix = GetPayloadValue(eventData, "destinationPrefix");
        return $"[Forward Start] -> {destPrefix}";
    }

    private static string FormatForwarderStop(EventWrittenEventArgs eventData)
    {
        var statusCode = GetPayloadValue(eventData, "statusCode");
        return $"[Forward Stop] <- Status: {statusCode}";
    }

    private static string FormatForwarderInvoke(EventWrittenEventArgs eventData)
    {
        var clusterId = GetPayloadValue(eventData, "clusterId");
        var routeId = GetPayloadValue(eventData, "routeId");
        var destId = GetPayloadValue(eventData, "destinationId");
        return $"[Forward Invoke] Route: {routeId}, Cluster: {clusterId}, Dest: {destId}";
    }

    private static string FormatForwarderStage(EventWrittenEventArgs eventData)
    {
        var stage = GetPayloadValue(eventData, "stage");
        var stageName = stage switch
        {
            "1" => "SendAsync",
            "2" => "WaitForResponse",
            "3" => "ResponseReceived",
            "4" => "ResponseCompleted",
            _ => $"Stage {stage}"
        };
        return $"[Forward Stage] {stageName}";
    }

    private static string FormatContentTransferred(EventWrittenEventArgs eventData)
    {
        var isRequest = GetPayloadValue(eventData, "isRequest");
        var contentLength = GetPayloadValue(eventData, "contentLength");
        var direction = isRequest == "True" ? "Request" : "Response";
        return $"[Content Transferred] {direction}: {contentLength} bytes";
    }

    private static string FormatDefaultEvent(EventWrittenEventArgs eventData)
    {
        var parts = new List<string> { $"[{eventData.EventName}]" };
        
        for (int i = 0; i < eventData.Payload.Count; i++)
        {
            var value = eventData.Payload[i];
            if (value != null)
            {
                var key = eventData.PayloadNames?.Count > i ? eventData.PayloadNames[i] : $"arg{i}";
                parts.Add($"{key}={value}");
            }
        }

        return string.Join(" ", parts);
    }

    private static string GetPayloadValue(EventWrittenEventArgs eventData, string key)
    {
        if (eventData.PayloadNames == null) return "?";
        
        var index = eventData.PayloadNames.IndexOf(key);
        if (index >= 0 && index < eventData.Payload.Count)
            return eventData.Payload[index]?.ToString() ?? "?";
        
        return "?";
    }

    /// <summary>
    /// Disposes the listener and disables event collection.
    /// </summary>
    public new void Dispose()
    {
        if (_yarpEventSource != null)
        {
            try
            {
                DisableEvents(_yarpEventSource);
            }
            catch
            {
                // Ignore errors during disposal
            }
        }
    }
}
