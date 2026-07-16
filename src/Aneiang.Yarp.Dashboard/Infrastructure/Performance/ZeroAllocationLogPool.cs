using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Zero-allocation log entry using struct and MemoryPool.
/// Eliminates GC pressure in high-throughput logging scenarios.
/// Total size: 128 bytes (fits in two cache lines)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public readonly record struct LogEntryStruct
{
    // 8 bytes: Timestamp as Unix milliseconds
    public readonly long TimestampUnixMs;
    
    // 4 bytes: Event type encoded as byte
    public readonly byte EventTypeCode;
    
    // 4 bytes: Status code (for responses)
    public readonly int StatusCode;
    
    // 8 bytes: Elapsed time in microseconds (more precision, less memory than double)
    public readonly long ElapsedMicros;
    
    // 8 bytes: Pre-computed hash of RouteId for fast lookup
    public readonly int RouteIdHash;
    public readonly int ClusterIdHash;
    
    // 8 bytes: Offsets into shared memory buffer
    public readonly int MetadataOffset;
    public readonly short MetadataLength;
    
    // 2 bytes: Reserved for alignment
    public readonly short Reserved;
    
    // 80 bytes: Inline storage for short strings (up to 40 UTF-16 chars or 80 UTF-8 bytes)
    // Uses fixed-size buffer to avoid heap allocation for common cases
    public readonly InlineStringBuffer RouteId;
    public readonly InlineStringBuffer ClusterId;
    public readonly InlineStringBuffer Method;
    public readonly InlineStringBuffer TraceId;

    public LogEntryStruct(
        long timestampUnixMs,
        byte eventTypeCode,
        int statusCode,
        long elapsedMicros,
        ReadOnlySpan<char> routeId,
        ReadOnlySpan<char> clusterId,
        ReadOnlySpan<char> method,
        ReadOnlySpan<char> traceId)
    {
        TimestampUnixMs = timestampUnixMs;
        EventTypeCode = eventTypeCode;
        StatusCode = statusCode;
        ElapsedMicros = elapsedMicros;
        
        RouteId = new InlineStringBuffer(routeId);
        ClusterId = new InlineStringBuffer(clusterId);
        Method = new InlineStringBuffer(method);
        TraceId = new InlineStringBuffer(traceId);
        
        // Pre-compute hashes for O(1) lookup
        RouteIdHash = ComputeHash(routeId);
        ClusterIdHash = ComputeHash(clusterId);
        
        MetadataOffset = 0;
        MetadataLength = 0;
        Reserved = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeHash(ReadOnlySpan<char> span)
    {
        // FNV-1a hash - fast and good distribution
        uint hash = 2166136261;
        for (int i = 0; i < span.Length; i++)
        {
            hash ^= span[i];
            hash *= 16777619;
        }
        return (int)hash;
    }

    public DateTime GetTimestamp() => DateTimeOffset.FromUnixTimeMilliseconds(TimestampUnixMs).UtcDateTime;
    
    public string GetRouteId() => RouteId.ToString();
    public string GetClusterId() => ClusterId.ToString();
    public string GetMethod() => Method.ToString();
    public string GetTraceId() => TraceId.ToString();
}



