using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Aneiang.Yarp.Infrastructure.Performance;

/// <summary>
/// Lock-free statistics accumulator using Interlocked operations.
/// Provides high-throughput concurrent counting with minimal contention.
/// Registered as singleton - every proxy request records via RecordRequest().
/// </summary>
public sealed class LockFreeStatistics
{
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

    private readonly long[] _latencyBuckets = new long[LatencyBucketCount];
    private const int LatencyBucketCount = 32;
    private static readonly int[] BucketThresholds = GenerateBuckets();

    private readonly ConcurrentIntDictionary _statusCodes = new();
    private readonly ConcurrentIntDictionary _routeCounts = new();
    private readonly ConcurrentIntDictionary _clusterCounts = new();

    private static int[] GenerateBuckets()
    {
        var buckets = new int[LatencyBucketCount];
        for (int i = 0; i < LatencyBucketCount; i++)
        {
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
        RecordLatency(latencyMicros);
        _statusCodes.Increment(statusCode);

        if (routeIdHash != 0)
            _routeCounts.Increment(routeIdHash);
        if (clusterIdHash != 0)
            _clusterCounts.Increment(clusterIdHash);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecordLatency(long latencyMicros)
    {
        int bucket = 0;
        long micros = latencyMicros;
        while (bucket < LatencyBucketCount - 1 && micros > BucketThresholds[bucket])
        {
            bucket++;
            micros >>= 1;
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

public sealed class ConcurrentIntDictionary
{
    private const int StripeCount = 16;
    private readonly Dictionary<int, long>[] _stripes;
    private readonly SpinLock[] _locks;

    public ConcurrentIntDictionary()
    {
        _stripes = new Dictionary<int, long>[StripeCount];
        _locks = new SpinLock[StripeCount];

        for (int i = 0; i < StripeCount; i++)
        {
            _stripes[i] = new Dictionary<int, long>();
            _locks[i] = new SpinLock(false);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetStripeIndex(int key) => (key * 31) & (StripeCount - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Increment(int key)
    {
        var stripeIdx = GetStripeIndex(key);
        var lockTaken = false;

        try
        {
            _locks[stripeIdx].Enter(ref lockTaken);
            var stripe = _stripes[stripeIdx];
            stripe[key] = stripe.TryGetValue(key, out var val) ? val + 1 : 1;
        }
        finally
        {
            if (lockTaken)
                _locks[stripeIdx].Exit(false);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long Get(int key)
    {
        var stripeIdx = GetStripeIndex(key);
        var lockTaken = false;

        try
        {
            _locks[stripeIdx].Enter(ref lockTaken);
            return _stripes[stripeIdx].TryGetValue(key, out var val) ? val : 0;
        }
        finally
        {
            if (lockTaken)
                _locks[stripeIdx].Exit(false);
        }
    }

    public KeyValuePair<int, long>[] ToArray()
    {
        var result = new List<KeyValuePair<int, long>>();

        for (int i = 0; i < StripeCount; i++)
        {
            var lockTaken = false;
            try
            {
                _locks[i].Enter(ref lockTaken);
                result.AddRange(_stripes[i]);
            }
            finally
            {
                if (lockTaken)
                    _locks[i].Exit(false);
            }
        }

        return result.ToArray();
    }

    public KeyValuePair<int, long>[] GetTopN(int n)
    {
        var all = ToArray();
        return all.OrderByDescending(x => x.Value).Take(n).ToArray();
    }

    public void Clear()
    {
        for (int i = 0; i < StripeCount; i++)
        {
            var lockTaken = false;
            try
            {
                _locks[i].Enter(ref lockTaken);
                _stripes[i].Clear();
            }
            finally
            {
                if (lockTaken)
                    _locks[i].Exit(false);
            }
        }
    }
}

public readonly struct StatisticsSnapshot
{
    public long TotalRequests { get; init; }
    public long SuccessCount { get; init; }
    public long ErrorCount { get; init; }
    public double SuccessRate { get; init; }
    public double ErrorRate { get; init; }
    public long AvgLatencyMicros { get; init; }
    public KeyValuePair<int, long>[] StatusCodes { get; init; }
    public KeyValuePair<int, long>[] TopRoutes { get; init; }
    public KeyValuePair<int, long>[] TopClusters { get; init; }
    public DateTime ComputedAt { get; init; }
}
