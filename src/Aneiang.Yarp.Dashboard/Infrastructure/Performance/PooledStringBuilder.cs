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



