namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Marks struct as readonly to enable JIT optimizations.
/// Prevents defensive copies and enables better register allocation.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
internal sealed class JitReadonlyAttribute : Attribute { }
