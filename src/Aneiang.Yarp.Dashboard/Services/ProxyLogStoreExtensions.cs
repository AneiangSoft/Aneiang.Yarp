using System.Collections.Concurrent;
using Aneiang.Yarp.Dashboard.Models;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Extension methods for IProxyLogStore to support real-time log streaming via WebSocket.
/// </summary>
public static class ProxyLogStoreExtensions
{
    private static readonly ConcurrentDictionary<IProxyLogStore, List<Action<LogEntry>>> _subscriberMap = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Subscribe to new log entries. Returns an IDisposable to unsubscribe.
    /// </summary>
    public static IDisposable OnNewEntry(this IProxyLogStore store, Action<LogEntry> callback)
    {
        var subscribers = _subscriberMap.GetOrAdd(store, _ => new List<Action<LogEntry>>());

        lock (_lock)
        {
            subscribers.Add(callback);
        }

        return new Subscription(store, callback);
    }

    /// <summary>
    /// Notify all subscribers of a new log entry. Called by ProxyLogStore.Add().
    /// </summary>
    internal static void NotifySubscribers(IProxyLogStore store, LogEntry entry)
    {
        if (!_subscriberMap.TryGetValue(store, out var subscribers))
            return;

        Action<LogEntry>[] callbacks;
        lock (_lock)
        {
            callbacks = subscribers.ToArray();
        }

        foreach (var callback in callbacks)
        {
            try
            {
                callback(entry);
            }
            catch
            {
                // Ignore subscriber errors
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly IProxyLogStore _store;
        private readonly Action<LogEntry> _callback;

        public Subscription(IProxyLogStore store, Action<LogEntry> callback)
        {
            _store = store;
            _callback = callback;
        }

        public void Dispose()
        {
            if (_subscriberMap.TryGetValue(_store, out var subscribers))
            {
                lock (_lock)
                {
                    subscribers.Remove(_callback);
                }
            }
        }
    }
}
