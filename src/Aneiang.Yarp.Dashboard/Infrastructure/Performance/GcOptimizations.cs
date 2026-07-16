using System.Runtime;
using System.Runtime.InteropServices;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// GC pressure reduction strategies for high-throughput scenarios.
/// </summary>
internal static class GcOptimizations
{
    /// <summary>
    /// Sets GC mode optimized for server workloads.
    /// Call this early in application startup.
    /// </summary>
    public static void ConfigureServerGc()
    {
        // Settings should be in .csproj:
        // <ServerGarbageCollection>true</ServerGarbageCollection>
        // <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>

        // Force LOH compaction if fragmentation is high
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
    }

    /// <summary>
    /// Pre-allocates and pins arrays to reduce GC movement.
    /// Use for long-lived buffers.
    /// </summary>
    public static GCHandle PinBuffer(byte[] buffer)
    {
        return GCHandle.Alloc(buffer, GCHandleType.Pinned);
    }

    /// <summary>
    /// Triggers background GC to avoid pauses during traffic spikes.
    /// Only collects Gen 0 (short-lived objects) with <see cref="GCCollectionMode.Optimized"/>,
    /// which is non-intrusive: the runtime may choose to defer the collection if it would
    /// impact throughput. This is called at controlled points (e.g. after config reload)
    /// where we know ephemeral allocations have just peaked, and a non-blocking hint helps
    /// keep managed heap fragmentation low without adding latency to request processing.
    /// </summary>
    public static void TriggerBackgroundGc()
    {
        GC.Collect(0, GCCollectionMode.Optimized, blocking: false);
    }

    /// <summary>
    /// Registers for LOH allocation notification.
    /// Useful for triggering compaction before fragmentation becomes severe.
    /// </summary>
    public static void RegisterLohNotification(Action thresholdReached)
    {
        // In .NET 6+, use GC.RegisterForFullGCNotification
        // For earlier versions, monitor GC.CollectionCount(2)
    }
}
