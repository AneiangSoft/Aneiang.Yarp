using System.Runtime.CompilerServices;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

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
