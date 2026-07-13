using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Tools;

/// <summary>
/// Lightweight wrapper around <see cref="JsonElement"/> that provides
/// convenient accessor methods for AI tool argument parsing.
/// Eliminates repetitive <c>TryGetProperty</c> / <c>ValueKind</c> checks.
/// </summary>
public readonly struct ToolArgs
{
    private readonly JsonElement _el;

    /// <summary>Parse a JSON string into ToolArgs.</summary>
    public ToolArgs(string json)
    {
        _el = string.IsNullOrEmpty(json)
            ? JsonDocument.Parse("{}").RootElement
            : JsonDocument.Parse(json).RootElement;
    }

    /// <summary>Wrap an existing JsonElement.</summary>
    public ToolArgs(JsonElement element) => _el = element;

    /// <summary>Get the underlying JsonElement.</summary>
    public JsonElement Element => _el;

    // ───────────── String ─────────────

    /// <summary>Required string property. Throws if missing.</summary>
    public string Get(string key) => _el.GetProperty(key).GetString()!;

    /// <summary>Optional string property. Returns null if missing or not a string.</summary>
    public string? GetString(string key) =>
        _el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    // ───────────── Int ─────────────

    /// <summary>Optional int with default.</summary>
    public int GetInt(string key, int defaultValue = 0) =>
        _el.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.GetInt32() : defaultValue;

    /// <summary>Optional nullable int (returns null when missing).</summary>
    public int? GetNullableInt(string key) =>
        _el.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.GetInt32() : (int?)null;

    // ───────────── Bool ─────────────

    /// <summary>Optional bool with default.</summary>
    public bool GetBool(string key, bool defaultValue = false) =>
        _el.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.GetBoolean() : defaultValue;

    // ───────────── Arrays ─────────────

    /// <summary>Optional int array with fallback defaults.</summary>
    public List<int> GetIntArray(string key, List<int>? defaults = null) =>
        _el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(e => e.GetInt32()).ToList()
            : defaults ?? new List<int>();

    /// <summary>Optional string array.</summary>
    public List<string> GetStringArray(string key) =>
        _el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
            : new List<string>();

    // ───────────── Object ─────────────

    /// <summary>Optional string-keyed string map from a JSON object property.</summary>
    public Dictionary<string, string> GetStringMap(string key)
    {
        var map = new Dictionary<string, string>();
        if (_el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in v.EnumerateObject())
                map[prop.Name] = prop.Value.GetString()!;
        }
        return map;
    }

    // ───────────── Checks ─────────────

    /// <summary>Whether a property exists.</summary>
    public bool Has(string key) => _el.TryGetProperty(key, out _);

    /// <summary>Whether a property exists and is not null.</summary>
    public bool HasValue(string key) =>
        _el.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null;
}
