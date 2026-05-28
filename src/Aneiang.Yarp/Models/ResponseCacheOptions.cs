namespace Aneiang.Yarp.Models;

/// <summary>Options for gateway response caching.</summary>
public class ResponseCacheOptions
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Gateway:ResponseCache";

    /// <summary>Enable response caching for proxy requests. Default: false.</summary>
    public bool Enabled { get; set; }

    /// <summary>Default cache duration for GET requests when no Cache-Control header is present. Default: 30 seconds.</summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum cache duration regardless of Cache-Control header. Default: 5 minutes.</summary>
    public TimeSpan MaxTtl { get; set; } = TimeSpan.FromSeconds(300);

    /// <summary>Maximum response body size to cache (in bytes). Responses larger than this are not cached. Default: 1MB.</summary>
    public long MaxBodySize { get; set; } = 1024 * 1024;

    /// <summary>Maximum number of cache entries. When full, least-recently-used entries are evicted. Default: 1000.</summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>Cache key components: "url", "query", "headers". Default: ["url", "query"].</summary>
    public List<string> CacheKeyComponents { get; set; } = ["url", "query"];

    /// <summary>HTTP methods eligible for caching. Default: ["GET", "HEAD"].</summary>
    public HashSet<string> CacheableMethods { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD" };

    /// <summary>Status codes eligible for caching. Default: [200, 203, 204, 206, 300, 301, 404, 405, 410, 414, 501].</summary>
    public HashSet<int> CacheableStatusCodes { get; set; } = new() { 200, 203, 204, 206, 300, 301, 404, 405, 410, 414, 501 };

    /// <summary>Route-specific cache overrides. Key: route ID prefix, Value: TTL override.</summary>
    public Dictionary<string, TimeSpan>? RouteOverrides { get; set; }

    /// <summary>Headers to vary cache key by (in addition to URL and query). Default: ["Accept", "Accept-Encoding"].</summary>
    public List<string>? VaryByHeaders { get; set; } = ["Accept", "Accept-Encoding"];
}
