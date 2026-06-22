using System.Text;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// A simple, high-performance pool for StringBuilder instances.
/// Reduces GC pressure in high-throughput string concatenation scenarios.
/// Thread-safe via ThreadStatic fallback.
/// </summary>
internal static class PooledStringBuilder
{
    // Maximum capacity to retain in the pool (prevents memory bloat)
    private const int MaxCapacity = 4096;
    private const int InitialCapacity = 256;

    // ThreadStatic for lock-free per-thread pooling
    [ThreadStatic]
    private static StringBuilder? _cachedInstance;

    /// <summary>
    /// Gets a StringBuilder from the pool or creates a new one.
    /// </summary>
    public static StringBuilder Rent()
    {
        var sb = _cachedInstance;
        if (sb != null)
        {
            _cachedInstance = null;
            return sb;
        }
        return new StringBuilder(InitialCapacity);
    }

    /// <summary>
    /// Returns a StringBuilder to the pool after extracting its string value.
    /// </summary>
    public static string ToStringAndReturn(StringBuilder sb)
    {
        var result = sb.ToString();
        Return(sb);
        return result;
    }

    /// <summary>
    /// Returns a StringBuilder to the pool for reuse.
    /// Clears the contents and checks capacity before caching.
    /// </summary>
    public static void Return(StringBuilder sb)
    {
        // Clear and check capacity
        if (sb.Capacity <= MaxCapacity)
        {
            sb.Clear();
            _cachedInstance = sb;
        }
        // If capacity is too large, let it be GC'd
    }
}

/// <summary>
/// Helper methods for common string building patterns.
/// </summary>
internal static class StringBuilderExtensions
{
    /// <summary>
    /// Appends a key-value pair with separator efficiently.
    /// </summary>
    public static StringBuilder AppendKeyValue(this StringBuilder sb, string key, string? value, string separator = ", ")
    {
        if (sb.Length > 0)
            sb.Append(separator);
        sb.Append(key).Append('=').Append(value ?? "null");
        return sb;
    }

    /// <summary>
    /// Appends a query string parameter.
    /// </summary>
    public static StringBuilder AppendQueryParam(this StringBuilder sb, string key, string? value, bool isFirst = false)
    {
        if (!isFirst)
            sb.Append('&');
        sb.Append(key).Append('=').Append(Uri.EscapeDataString(value ?? ""));
        return sb;
    }

    /// <summary>
    /// Appends a JSON property name and value.
    /// </summary>
    public static StringBuilder AppendJsonProperty(this StringBuilder sb, string name, string? value, bool isFirst = false)
    {
        if (!isFirst)
            sb.Append(',');
        sb.Append('"').Append(name).Append("\":\"");
        if (!string.IsNullOrEmpty(value))
        {
            // Basic JSON escaping
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
        }
        sb.Append('"');
        return sb;
    }
}


