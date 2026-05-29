using System.Collections.Concurrent;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Services.Implements;

/// <summary>In-memory LRU response cache for proxy responses.</summary>
public sealed class ResponseCacheService : IResponseCacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly LinkedList<string> _lruList = new();
    private readonly object _lruLock = new();
    private readonly ILogger<ResponseCacheService> _logger;
    private readonly int _maxEntries;
    private long _hits;
    private long _misses;

    public ResponseCacheService(IOptions<ResponseCacheOptions> options, ILogger<ResponseCacheService> logger)
    {
        _maxEntries = options.Value.MaxEntries;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool TryGet(string key, out byte[] body, out Dictionary<string, string> headers, out int statusCode)
    {
        body = [];
        headers = new();
        statusCode = 0;

        if (!_cache.TryGetValue(key, out var entry))
        {
            Interlocked.Increment(ref _misses);
            return false;
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            RemoveEntry(key, entry);
            Interlocked.Increment(ref _misses);
            return false;
        }

        lock (_lruLock)
        {
            if (entry.LruNode?.List != null)
            {
                _lruList.Remove(entry.LruNode);
                _lruList.AddFirst(entry.LruNode);
            }
        }

        Interlocked.Increment(ref _hits);
        body = entry.Body;
        headers = entry.Headers;
        statusCode = entry.StatusCode;
        return true;
    }

    /// <inheritdoc />
    public void Set(string key, byte[] body, Dictionary<string, string> headers, int statusCode, TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero) return;

        var entry = new CacheEntry
        {
            Body = body,
            Headers = headers,
            StatusCode = statusCode,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl)
        };

        lock (_lruLock)
        {
            entry.LruNode = _lruList.AddFirst(key);
        }

        var existing = _cache.GetOrAdd(key, entry);
        if (existing != entry)
        {
            lock (_lruLock)
            {
                if (existing.LruNode?.List != null)
                    _lruList.Remove(existing.LruNode);
                entry.LruNode = _lruList.AddFirst(key);
            }
            _cache[key] = entry;
        }

        EvictIfNeeded();

        _logger.LogDebug("Cached entry {Key} (TTL: {Ttl}s, Size: {Size} bytes)", key, ttl.TotalSeconds, body.Length);
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        if (_cache.TryRemove(key, out var entry))
        {
            RemoveLruNode(entry);
        }
    }

    /// <inheritdoc />
    public void RemoveByPrefix(string routeId)
    {
        foreach (var kvp in _cache)
        {
            if (kvp.Key.StartsWith(routeId, StringComparison.OrdinalIgnoreCase))
            {
                if (_cache.TryRemove(kvp.Key, out var entry))
                {
                    RemoveLruNode(entry);
                }
            }
        }

        _logger.LogInformation("Invalidated cache entries for route prefix '{RouteId}'", routeId);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _cache.Clear();
        lock (_lruLock)
        {
            _lruList.Clear();
        }

        _logger.LogInformation("Response cache cleared");
    }

    /// <inheritdoc />
    public Dictionary<string, object> GetStats()
    {
        long hits = Interlocked.Read(ref _hits);
        long misses = Interlocked.Read(ref _misses);
        long total = hits + misses;
        long estimatedSize = 0;
        int count = 0;

        foreach (var kvp in _cache)
        {
            estimatedSize += kvp.Value.Body.Length;
            count++;
        }

        return new Dictionary<string, object>
        {
            ["entryCount"] = count,
            ["maxEntries"] = _maxEntries,
            ["hits"] = hits,
            ["misses"] = misses,
            ["hitRate"] = total > 0 ? Math.Round((double)hits / total * 100, 2) : 0.0,
            ["estimatedSizeBytes"] = estimatedSize,
            ["estimatedSizeMB"] = Math.Round((double)estimatedSize / (1024 * 1024), 2)
        };
    }

    private void EvictIfNeeded()
    {
        while (_cache.Count > _maxEntries)
        {
            lock (_lruLock)
            {
                if (_lruList.Last == null) break;
                var lruKey = _lruList.Last.Value;
                _lruList.RemoveLast();

                if (_cache.TryRemove(lruKey, out var entry))
                {
                    entry.LruNode = null;
                }
            }
        }
    }

    private void RemoveEntry(string key, CacheEntry entry)
    {
        _cache.TryRemove(key, out _);
        RemoveLruNode(entry);
    }

    private void RemoveLruNode(CacheEntry entry)
    {
        lock (_lruLock)
        {
            if (entry.LruNode?.List != null)
            {
                _lruList.Remove(entry.LruNode);
            }
        }
    }

    internal sealed class CacheEntry
    {
        public byte[] Body = [];
        public Dictionary<string, string> Headers = new();
        public int StatusCode;
        public DateTimeOffset ExpiresAt;
        public LinkedListNode<string>? LruNode;
    }
}
