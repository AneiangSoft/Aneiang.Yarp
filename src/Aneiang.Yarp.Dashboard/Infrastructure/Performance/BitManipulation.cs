using System.Runtime.CompilerServices;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Bit manipulation utilities for compact storage.
/// </summary>
internal static class BitManipulation
{
    /// <summary>
    /// Pack multiple small integers into a single long.
    /// Useful for reducing memory footprint of composite IDs.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PackInts(int a, int b, int c, int d)
    {
        return ((ulong)(uint)a << 48) |
               ((ulong)(uint)b << 32) |
               ((ulong)(uint)c << 16) |
               (ulong)(uint)d;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UnpackInts(ulong packed, out int a, out int b, out int c, out int d)
    {
        a = (int)(packed >> 48);
        b = (int)(packed >> 32);
        c = (int)(packed >> 16);
        d = (int)packed;
    }

    /// <summary>
    /// Count leading zeros using hardware instruction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountLeadingZeros(uint value)
    {
        if (System.Runtime.Intrinsics.X86.Lzcnt.IsSupported)
        {
            return (int)System.Runtime.Intrinsics.X86.Lzcnt.LeadingZeroCount(value);
        }

        // Software fallback
        if (value == 0) return 32;
        int count = 0;
        while ((value & 0x80000000) == 0)
        {
            count++;
            value <<= 1;
        }
        return count;
    }

    /// <summary>
    /// Bit scan reverse (position of highest set bit).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BitScanReverse(uint value)
    {
        if (System.Runtime.Intrinsics.X86.Bmi1.IsSupported)
        {
            return 31 - (int)System.Runtime.Intrinsics.X86.Lzcnt.LeadingZeroCount(value);
        }

        int pos = 0;
        while (value > 1)
        {
            value >>= 1;
            pos++;
        }
        return pos;
    }

    /// <summary>
    /// Population count (number of set bits).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopCount(ulong value)
    {
        if (System.Runtime.Intrinsics.X86.Popcnt.X64.IsSupported)
        {
            return (int)System.Runtime.Intrinsics.X86.Popcnt.X64.PopCount(value);
        }

        // Software fallback: parallel bit count
        const ulong c1 = 0x5555555555555555;
        const ulong c2 = 0x3333333333333333;
        const ulong c4 = 0x0F0F0F0F0F0F0F0F;

        value -= (value >> 1) & c1;
        value = (value & c2) + ((value >> 2) & c2);
        value = (value + (value >> 4)) & c4;
        value += value >> 8;
        value += value >> 16;
        value += value >> 32;
        return (int)(value & 0x7F);
    }
}
