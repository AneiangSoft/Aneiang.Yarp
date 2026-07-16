using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Lock-free statistics accumulator using Interlocked operations.
/// Provides high-throughput concurrent counting with minimal contention.
/// Registered as singleton — every proxy request records via RecordRequest().
/// </summary>
public sealed class LockFreeStatistics
{
    // Per-counter aligned storage to prevent false sharing
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct AlignedCounter
    {
        [FieldOffset(64)]
        public long Value;
    }

    private AlignedCounter _totalRequests;
    private AlignedCounter _successCount;
    private AlignedCounter _errorCount;
    private AlignedCounter _totalLatencyMicros;

    // Latency histogram for percentile calculation (lock-free)
    private readonly long[] _latencyBuckets = new long[LatencyBucketCount];
    private const int LatencyBucketCount = 32;
    private static readonly int[] BucketThresholds = GenerateBuckets();

    // Status code counters using concurrent dictionary
    private readonly ConcurrentIntDictionary _statusCodes = new();
    private readonly ConcurrentIntDictionary _routeCounts = new();
    private readonly ConcurrentIntDictionary _clusterCounts = new();

    private static int[] GenerateBuckets()
    {
        var buckets = new int[LatencyBucketCount];
        for (int i = 0; i < LatencyBucketCount; i++)
        {
            // Exponential bucketing: 0-1ms, 1-2ms, 2-4ms, 4-8ms, etc.
            buckets[i] = i == 0 ? 1000 : (1 << i) * 1000;
        }
        return buckets;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordRequest(int statusCode, long latencyMicros, int routeIdHash, int clusterIdHash)
    {
        Interlocked.Increment(ref _totalRequests.Value);

        if (statusCode >= 200 && statusCode < 400)
            Interlocked.Increment(ref _successCount.Value);
        else if (statusCode >= 400)
            Interlocked.Increment(ref _errorCount.Value);

        Interlocked.Add(ref _totalLatencyMicros.Value, latencyMicros);

        // Record to latency histogram
        RecordLatency(latencyMicros);

        // Record status code
        _statusCodes.Increment(statusCode);

        // Record route/cluster (using hash as key)
        if (routeIdHash != 0)
            _routeCounts.Increment(routeIdHash);
        if (clusterIdHash != 0)
            _clusterCounts.Increment(clusterIdHash);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordLatency(long latencyMicros)
    {
        // Find bucket using bit manipulation (fast for power-of-2 boundaries)
        int bucket = 0;
        long micros = latencyMicros;
        while (bucket < LatencyBucketCount - 1 && micros > BucketThresholds[bucket])
        {
            bucket++;
            micros >>= 1; // Divide by 2
        }

        Interlocked.Increment(ref _latencyBuckets[bucket]);
    }

    public StatisticsSnapshot GetSnapshot()
    {
        var total = Interlocked.Read(ref _totalRequests.Value);
        var success = Interlocked.Read(ref _successCount.Value);
        var error = Interlocked.Read(ref _errorCount.Value);
        var totalLatency = Interlocked.Read(ref _totalLatencyMicros.Value);

        return new StatisticsSnapshot
        {
            TotalRequests = total,
            SuccessCount = success,
            ErrorCount = error,
            SuccessRate = total > 0 ? (double)success / total * 100 : 0,
            ErrorRate = total > 0 ? (double)error / total * 100 : 0,
            AvgLatencyMicros = total > 0 ? totalLatency / total : 0,
            StatusCodes = _statusCodes.ToArray(),
            TopRoutes = _routeCounts.GetTopN(10),
            TopClusters = _clusterCounts.GetTopN(10),
            ComputedAt = DateTime.Now
        };
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _totalRequests.Value, 0);
        Interlocked.Exchange(ref _successCount.Value, 0);
        Interlocked.Exchange(ref _errorCount.Value, 0);
        Interlocked.Exchange(ref _totalLatencyMicros.Value, 0);
        Array.Clear(_latencyBuckets, 0, _latencyBuckets.Length);
        _statusCodes.Clear();
        _routeCounts.Clear();
        _clusterCounts.Clear();
    }
}



