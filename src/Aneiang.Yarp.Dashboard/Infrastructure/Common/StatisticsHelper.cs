using System.Runtime.InteropServices;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Common;

/// <summary>
/// Shared statistical utility methods extracted from controllers
/// to eliminate duplicate percentile-calculation code.
/// </summary>
public static class StatisticsHelper
{
    /// <summary>
    /// Calculate the p-th percentile from a pre-sorted list using linear interpolation.
    /// </summary>
    /// <param name="sorted">Ascending-sorted list of values.</param>
    /// <param name="p">Percentile in range [0, 1] (e.g. 0.50 for P50).</param>
    public static double CalculatePercentileSorted(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];

        // Use span for zero-allocation indexing when available
        var span = CollectionsMarshal.AsSpan(sorted);
        var idx = p * (span.Length - 1);
        var lower = (int)Math.Floor(idx);
        var upper = (int)Math.Ceiling(idx);

        if (lower == upper) return span[lower];
        return span[lower] + (span[upper] - span[lower]) * (idx - lower);
    }

    /// <summary>
    /// Merge source dictionary into target dictionary, summing values for shared keys.
    /// </summary>
    public static void MergeDictionaries<TKey>(Dictionary<TKey, int> target, Dictionary<TKey, int> source)
        where TKey : notnull
    {
        foreach (var kvp in source)
        {
            if (!target.TryGetValue(kvp.Key, out var existing))
                target[kvp.Key] = kvp.Value;
            else
                target[kvp.Key] = existing + kvp.Value;
        }
    }
}
