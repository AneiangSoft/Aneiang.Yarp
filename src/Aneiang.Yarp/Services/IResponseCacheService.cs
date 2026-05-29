namespace Aneiang.Yarp.Services;

/// <summary>
/// Interface for in-memory LRU response cache for proxy responses.
/// </summary>
public interface IResponseCacheService
{
    /// <summary>Try to retrieve a cached response.</summary>
    bool TryGet(string key, out byte[] body, out Dictionary<string, string> headers, out int statusCode);

    /// <summary>Store a response in the cache.</summary>
    void Set(string key, byte[] body, Dictionary<string, string> headers, int statusCode, TimeSpan ttl);

    /// <summary>Remove a specific cache entry.</summary>
    void Remove(string key);

    /// <summary>Remove all cache entries whose key starts with the given prefix.</summary>
    void RemoveByPrefix(string routeId);

    /// <summary>Clear all cache entries.</summary>
    void Clear();

    /// <summary>Get cache statistics.</summary>
    Dictionary<string, object> GetStats();
}
