using System.Runtime.CompilerServices;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Assembly-level initialization for JIT optimizations.
/// </summary>
internal static class JitOptimizationInitializer
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        // Warm up critical paths on module load
        JitWarmup.WarmupCriticalMethods();
    }
}
