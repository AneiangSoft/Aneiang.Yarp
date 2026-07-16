using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Concurrent int-int dictionary using striped locking.
/// Optimized for high-contention counting scenarios.
/// </summary>
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
