namespace Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;

public sealed class WafCheckResult
{
    public static readonly WafCheckResult Passed = new() { IsBlocked = false };

    public bool IsBlocked { get; init; }
    public string? EventType { get; init; }
    public string? Details { get; init; }

    public static WafCheckResult Blocked(string eventType, string? details = null) =>
        new() { IsBlocked = true, EventType = eventType, Details = details };
}
