using System.Diagnostics.Tracing;
using Aneiang.Yarp.Dashboard.Models;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Listens to YARP's internal EventSource events and writes them to the ProxyLogStore.
/// This works regardless of the logging framework (Serilog, NLog, etc.) being used.
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
            var level = YarpEventFormatter.MapEventLevelToLogLevel(eventData.Level);
            
            // Filter: only capture Information and above (same as ILoggerProvider)
            if (level < Microsoft.Extensions.Logging.LogLevel.Information) return;

            var message = YarpEventFormatter.FormatEventMessage(eventData);
            if (string.IsNullOrEmpty(message)) return;

            _store.Add(new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level.ToString(),
                Category = eventData.EventSource.Name,
                Message = message,
                Exception = null // EventSource events typically don't include exceptions
            });
        }
        catch (Exception)
        {
            // Silently ignore errors in event processing to avoid recursion
        }
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
            catch (ObjectDisposedException)
            {
                // EventSource already disposed, ignore
            }
            catch (Exception)
            {
                // Ignore other errors during disposal
            }
        }

        base.Dispose();
    }
}
