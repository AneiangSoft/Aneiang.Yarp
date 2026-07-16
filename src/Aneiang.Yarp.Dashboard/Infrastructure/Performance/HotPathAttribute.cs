namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// Marks method for aggressive JIT inlining.
/// Only use on hot paths where call overhead matters.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class HotPathAttribute : Attribute
{
    public string? Description { get; }
    public HotPathAttribute(string? description = null) => Description = description;
}
