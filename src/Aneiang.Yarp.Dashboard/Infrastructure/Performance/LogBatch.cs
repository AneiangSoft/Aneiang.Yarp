using System.Runtime.CompilerServices;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

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
