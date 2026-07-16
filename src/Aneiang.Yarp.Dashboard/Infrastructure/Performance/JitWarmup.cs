using System.Runtime.CompilerServices;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Pre-JITs critical methods at startup to avoid cold-start latency.
/// </summary>
internal static class JitWarmup
{
    /// <summary>
    /// Warms up all methods marked with [PreJit] attribute.
    /// Call this during application startup.
    /// </summary>
    public static void WarmupCriticalMethods()
    {
        // These calls force JIT compilation of hot paths

        // String operations
        JitOptimizedHotPaths.FastEqualsOrdinal("warmup", "warmup");
        JitOptimizedHotPaths.FastEqualsAsciiIgnoreCase("GET"u8.ToArray(), "get"u8.ToArray());

        // Hashing
        JitOptimizedHotPaths.FastHash64("warmup"u8.ToArray());
        JitOptimizedHotPaths.FastStringHash("warmup");

        // Memory
        Span<byte> buffer = stackalloc byte[64];
        JitOptimizedHotPaths.FastClear(buffer);

        // Math
        JitOptimizedHotPaths.ClampBranchless(50, 0, 100);

        // Trigger static constructor of optimized types
        RuntimeHelpers.RunClassConstructor(typeof(JitOptimizedHotPaths).TypeHandle);
    }
}
