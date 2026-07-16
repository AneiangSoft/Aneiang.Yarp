namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Marks method for tiered compilation warmup.
/// Ensures method is JITted at startup, not during first request.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class PreJitAttribute : Attribute { }
