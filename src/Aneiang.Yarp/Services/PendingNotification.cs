namespace Aneiang.Yarp.Services;

public readonly struct PendingNotification
{
    public string EventType { get; init; }
    public string Target { get; init; }
    public string? Operator { get; init; }
    public object? Details { get; init; }
}
