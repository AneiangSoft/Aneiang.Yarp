using System.Diagnostics.Tracing;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>Formats YARP EventSource events into human-readable log messages.</summary>
internal static class YarpEventFormatter
{
    /// <summary>Maps EventLevel to Microsoft.Extensions.Logging.LogLevel.</summary>
    public static Microsoft.Extensions.Logging.LogLevel MapEventLevelToLogLevel(EventLevel level)
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

    /// <summary>Formats an EventWrittenEventArgs into a readable message.</summary>
    public static string FormatEventMessage(EventWrittenEventArgs eventData)
    {
        if (eventData.Payload == null || eventData.Payload.Count == 0)
            return eventData.EventName ?? "Unknown";

        var eventName = eventData.EventName ?? "Unknown";

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
        var payloadCount = eventData.Payload?.Count ?? 0;
        var parts = new List<string>(payloadCount + 1);
        parts.Add($"[{eventData.EventName}]");

        if (eventData.Payload != null)
        {
            for (int i = 0; i < payloadCount; i++)
            {
                var value = eventData.Payload[i];
                if (value != null)
                {
                    var key = eventData.PayloadNames?.Count > i ? eventData.PayloadNames[i] : $"arg{i}";
                    parts.Add($"{key}={value}");
                }
            }
        }

        return string.Join(" ", parts);
    }

    private static string GetPayloadValue(EventWrittenEventArgs eventData, string key)
    {
        if (eventData.PayloadNames == null || eventData.Payload == null) return "?";

        var index = eventData.PayloadNames.IndexOf(key);
        if (index >= 0 && index < eventData.Payload.Count)
            return eventData.Payload[index]?.ToString() ?? "?";

        return "?";
    }
}
