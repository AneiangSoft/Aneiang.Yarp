using System.Buffers;
using System.Runtime.CompilerServices;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

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
