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

/// <summary>
/// Custom JSON converter that serializes HeaderList as {"key":"value"} object format
/// and deserializes back — maintaining backward compatibility with the Dictionary format
/// used by frontend and existing SQLite data.
/// </summary>
public sealed class HeaderListJsonConverter : JsonConverter<HeaderList?>
{
    public override HeaderList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        // Read as JSON object {"key":"value"}
        using var doc = JsonDocument.ParseValue(ref reader);
        var list = new HeaderList(doc.RootElement.EnumerateObject().Count());

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var value = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetString();
            list.Add(prop.Name, value ?? string.Empty);
        }

        return list;
    }

    public override void Write(Utf8JsonWriter writer, HeaderList? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        // Write as JSON object {"key":"value"} — same format as Dictionary<string,string>
        writer.WriteStartObject();
        foreach (var kvp in value)
        {
            writer.WriteString(kvp.Key, kvp.Value);
        }
        writer.WriteEndObject();
    }
}
