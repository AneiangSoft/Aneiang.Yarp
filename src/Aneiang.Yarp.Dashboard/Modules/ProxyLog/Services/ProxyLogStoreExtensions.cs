using System.Collections.Concurrent;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Extension methods for IProxyLogStore to support real-time log streaming via WebSocket.
/// Memory optimization (v2.4): Uses immutable array swap pattern instead of lock+ToArray()
/// for lock-free reads on the hot path (NotifySubscribers). Write operations (subscribe/unsubscribe)
/// use ConcurrentDictionary.AddOrUpdate with copy-on-write semantics.
/// </summary>
internal static class ProxyLogStoreExtensions
{
    private static readonly ConcurrentDictionary<IProxyLogStore, Action<LogEntry>[]?> _subscriberMap = new();

    /// <summary>
    /// Subscribe to new log entries. Returns an IDisposable to unsubscribe.
    /// Uses copy-on-write: creates a new array with the callback appended and atomically swaps the reference.
    /// </summary>
    public static IDisposable OnNewEntry(this IProxyLogStore store, Action<LogEntry> callback)
    {
        _subscriberMap.AddOrUpdate(store,
            _ => new Action<LogEntry>[] { callback },
            (_, existing) =>
            {
                if (existing == null || existing.Length == 0)
                    return new Action<LogEntry>[] { callback };
                var newArr = new Action<LogEntry>[existing.Length + 1];
                existing.CopyTo(newArr, 0);
                newArr[existing.Length] = callback;
                return newArr;
            });

        return new Subscription(store, callback);
    }

    /// <summary>
    /// Notify all subscribers of a new log entry. Called by ProxyLogStore.Add().
    /// Lock-free: reads the immutable array reference via TryGetValue, no lock contention.
    /// </summary>
    internal static void NotifySubscribers(IProxyLogStore store, LogEntry entry)
    {
        if (!_subscriberMap.TryGetValue(store, out var subscribers) || subscribers == null)
            return;

        foreach (var callback in subscribers)
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
        private bool _disposed;

        public Subscription(IProxyLogStore store, Action<LogEntry> callback)
        {
            _store = store;
            _callback = callback;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _subscriberMap.AddOrUpdate(_store,
                _ => Array.Empty<Action<LogEntry>>(),
                (_, existing) =>
                {
                    if (existing == null || existing.Length == 0)
                        return Array.Empty<Action<LogEntry>>();
                    var newArr = new Action<LogEntry>[existing.Length - 1];
                    int j = 0;
                    for (int i = 0; i < existing.Length; i++)
                        if (existing[i] != _callback)
                            newArr[j++] = existing[i];
                    return newArr.Length == 0 ? Array.Empty<Action<LogEntry>>() : newArr;
                });

            // Clean up empty subscriber lists to prevent memory leak
            if (_subscriberMap.GetValueOrDefault(_store)?.Length == 0)
                _subscriberMap.TryRemove(_store, out _);
        }
    }
}

