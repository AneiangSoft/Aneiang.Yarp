using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Cache-optimized circular buffer using contiguous memory.
/// Prefetching and cache-line alignment for maximum throughput.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CacheOptimizedRingBuffer<T> where T : unmanaged
{
    private readonly T* _buffer;
    private readonly int _capacity;
    private readonly int _mask; // For power-of-2 optimization
    private long _head;
    private long _tail;

    public int Capacity => _capacity;
    public int Count => (int)(_head - _tail);
    public bool IsEmpty => _head == _tail;
    public bool IsFull => Count >= _capacity;

    public CacheOptimizedRingBuffer(int capacity)
    {
        // Round up to power of 2 for fast modulo
        capacity = UnsafeOptimizations.NextPowerOf2(capacity);
        _capacity = capacity;
        _mask = capacity - 1;
        _head = 0;
        _tail = 0;

        // Allocate aligned memory
        _buffer = (T*)NativeMemory.AlignedAlloc((nuint)(capacity * sizeof(T)), 64); // 64-byte cache line alignment
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnqueue(in T item)
    {
        if (IsFull) return false;

        var index = (int)(_head & _mask);
        _buffer[index] = item;
        Interlocked.Increment(ref _head);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDequeue(out T item)
    {
        if (IsEmpty)
        {
            item = default;
            return false;
        }

        var index = (int)(_tail & _mask);
        item = _buffer[index];
        Interlocked.Increment(ref _tail);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Peek()
    {
        if (IsEmpty) throw new InvalidOperationException("Buffer empty");
        var index = (int)(_tail & _mask);
        return _buffer[index];
    }

    public void Clear()
    {
        _tail = _head;
    }

    public void Dispose()
    {
        if (_buffer != null)
        {
            NativeMemory.AlignedFree(_buffer);
        }
    }
}
