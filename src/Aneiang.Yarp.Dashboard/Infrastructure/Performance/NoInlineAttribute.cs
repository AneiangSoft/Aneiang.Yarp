namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Hints to JIT that method should not be inlined if it increases code size significantly.
/// Use for large methods that are not extremely hot.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class NoInlineAttribute : Attribute { }
