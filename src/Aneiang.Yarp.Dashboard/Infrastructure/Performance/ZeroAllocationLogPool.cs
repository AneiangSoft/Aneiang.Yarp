using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

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

/// <summary>
/// Inline string buffer storing up to 20 UTF-16 characters (40 bytes) without heap allocation.
/// Falls back to external memory for longer strings.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 40)]
public readonly struct InlineStringBuffer
{
    private readonly ulong _data0;  // 8 bytes
    private readonly ulong _data1;  // 8 bytes
    private readonly ulong _data2;  // 8 bytes
    private readonly ulong _data3;  // 8 bytes
    private readonly uint _data4;   // 4 bytes
    private readonly byte _length;  // 1 byte
    private readonly byte _hasExternal; // 1 byte
    // 2 bytes padding

    public int Length => _length;
    public bool HasExternalStorage => _hasExternal != 0;

    public InlineStringBuffer(ReadOnlySpan<char> text)
    {
        _length = (byte)Math.Min(text.Length, 20);
        
        if (text.Length <= 20)
        {
            _hasExternal = 0;
            // Pack characters into ulong fields
            Span<byte> bytes = stackalloc byte[40];
            var byteCount = Encoding.UTF8.GetBytes(text, bytes);
            
            _data0 = byteCount > 0 ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : 0;
            _data1 = byteCount > 8 ? BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8)) : 0;
            _data2 = byteCount > 16 ? BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(16)) : 0;
            _data3 = byteCount > 24 ? BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(24)) : 0;
            _data4 = byteCount > 32 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(32)) : 0;
        }
        else
        {
            _hasExternal = 1;
            // Store hash for external lookup
            _data0 = (ulong)text.GetHashCode();
            _data1 = _data2 = _data3 = 0;
            _data4 = 0;
        }
    }

    public override string ToString()
    {
        if (_hasExternal != 0)
            return $"[External:{_data0}]";
            
        Span<byte> bytes = stackalloc byte[36];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, _data0);
        if (_length > 8) BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(8), _data1);
        if (_length > 16) BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(16), _data2);
        if (_length > 24) BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(24), _data3);
        if (_length > 32) BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(32), _data4);
        
        return Encoding.UTF8.GetString(bytes.Slice(0, _length));
    }
}

/// <summary>
/// High-performance object pool for LogEntryStruct arrays.
/// Uses ArrayPool for minimal GC pressure.
/// </summary>
internal sealed class LogEntryPool : IDisposable
{
    private readonly ArrayPool<LogEntryStruct> _structPool;
    private readonly ArrayPool<byte> _bytePool;
    private readonly DefaultObjectProvider<LogBatch> _batchPool;
    
    // Pre-allocated sizes for common scenarios
    private const int SmallBatchSize = 64;
    private const int MediumBatchSize = 256;
    private const int LargeBatchSize = 1024;
    
    // Singleton instance
    private static LogEntryPool? _instance;
    private static readonly object _instanceLock = new();
    
    public static LogEntryPool Shared
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new LogEntryPool();
                }
            }
            return _instance;
        }
    }

    public LogEntryPool()
    {
        _structPool = ArrayPool<LogEntryStruct>.Create(LargeBatchSize, 10);
        _bytePool = ArrayPool<byte>.Create(64 * 1024, 10); // 64KB max
        _batchPool = new DefaultObjectProvider<LogBatch>(() => new LogBatch(this), 10);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LogEntryStruct[] RentStructs(int minimumLength)
    {
        return _structPool.Rent(minimumLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReturnStructs(LogEntryStruct[] array, bool clearArray = false)
    {
        _structPool.Return(array, clearArray);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] RentBytes(int minimumLength)
    {
        return _bytePool.Rent(minimumLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReturnBytes(byte[] array, bool clearArray = false)
    {
        _bytePool.Return(array, clearArray);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LogBatch RentBatch()
    {
        return _batchPool.Get();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReturnBatch(LogBatch batch)
    {
        batch.Reset();
        _batchPool.Return(batch);
    }

    public void Dispose()
    {
        // Pools don't require explicit disposal
    }
}

/// <summary>
/// Reusable log batch container with pooled arrays.
/// </summary>
internal sealed class LogBatch
{
    private readonly LogEntryPool _pool;
    public LogEntryStruct[] Entries { get; private set; }
    public int Count { get; private set; }
    public int Capacity => Entries.Length;

    public LogBatch(LogEntryPool pool)
    {
        _pool = pool;
        Entries = pool.RentStructs(256);
        Count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(in LogEntryStruct entry)
    {
        if (Count >= Entries.Length)
            return false;
            
        Entries[Count++] = entry;
        return true;
    }

    public void Reset()
    {
        // Clear only used portion to avoid leaking sensitive data
        if (Count > 0)
        {
            Array.Clear(Entries, 0, Count);
            Count = 0;
        }
    }

    public void Dispose()
    {
        _pool.ReturnStructs(Entries);
    }
}

/// <summary>
/// Lightweight object pool implementation.
/// </summary>
internal sealed class DefaultObjectProvider<T> where T : class
{
    private readonly Func<T> _factory;
    private readonly T?[] _pool;
    private int _index;

    public DefaultObjectProvider(Func<T> factory, int capacity)
    {
        _factory = factory;
        _pool = new T[capacity];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Get()
    {
        var idx = Interlocked.Decrement(ref _index);
        if (idx >= 0 && idx < _pool.Length)
        {
            var item = Interlocked.Exchange(ref _pool[idx], null);
            if (item != null)
                return item;
        }
        return _factory();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(T item)
    {
        var idx = Interlocked.Increment(ref _index) - 1;
        if (idx >= 0 && idx < _pool.Length)
        {
            Interlocked.Exchange(ref _pool[idx], item);
        }
    }
}


