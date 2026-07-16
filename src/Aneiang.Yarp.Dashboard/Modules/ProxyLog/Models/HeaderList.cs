using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

/// <summary>
/// Lightweight header list — replaces Dictionary&lt;string, string&gt; to eliminate
/// Dictionary hash table overhead (buckets + entries arrays).
/// 
/// For 5-10 headers, a List of KeyValuePair saves ~200-400 bytes per instance:
/// - Dictionary: int[] buckets (prime-sized) + Entry[] entries (~40 bytes each)
/// - List: just a flat array of KeyValuePair (16 bytes each = 2 string refs)
/// 
/// Serialized as JSON object {"key":"value"} for frontend compatibility
/// via HeaderListJsonConverter — no frontend changes required.
/// </summary>
[JsonConverter(typeof(HeaderListJsonConverter))]
public sealed class HeaderList : List<KeyValuePair<string, string>>
{
    public HeaderList() : base() { }
    public HeaderList(int capacity) : base(capacity) { }

    /// <summary>Add a header key-value pair.</summary>
    public void Add(string key, string value) => Add(new KeyValuePair<string, string>(key, value));

    /// <summary>Find a value by key (linear search — O(n) but n ≤ 10 in practice).</summary>
    public string? FindValue(string key)
    {
        foreach (var kvp in this)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }
        return null;
    }
}

