using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Helper methods for efficient collection operations using low-level APIs.
/// </summary>
public static class CollectionsMarshalHelper
{
    /// <summary>
    /// Gets a Span view of a List's underlying array (zero-allocation).
    /// Uses CollectionsMarshal.AsSpan for direct array access.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<T> AsSpan<T>(List<T> list)
    {
        return CollectionsMarshal.AsSpan(list);
    }

    /// <summary>
    /// Adds a value to a dictionary or increments existing value.
    /// Uses GetValueRefOrAddDefault for efficient single-lookup operation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddOrIncrement<TKey>(Dictionary<TKey, int> dict, TKey key, int increment = 1)
        where TKey : notnull
    {
        ref var value = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out _);
        value += increment;
    }

    /// <summary>
    /// Gets a reference to a value in the dictionary or adds default.
    /// Allows direct modification without double lookup.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref TValue? GetValueRefOrNullAdd<TKey, TValue>(Dictionary<TKey, TValue> dict, TKey key)
        where TKey : notnull
    {
        return ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out _);
    }
}
