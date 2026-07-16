using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Services;

public sealed class NullNotificationService : INotificationService
{
    public static readonly NullNotificationService Instance = new();

    private NullNotificationService() { }

    public Task PreloadAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void InvalidateCache() { }
    public Task NotifyAsync(NotificationEvent evt, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> TestChannelAsync(string channelId, CancellationToken ct = default) => Task.FromResult(false);
    public void NotifyCircuitBreakerOpen(string clusterId, string? destinationId = null) { }
    public void NotifyRetryExhausted(string clusterId, string routeId, int attempts, int statusCode) { }
    public void NotifyWafBlock(string clientIp, string blockReason, string? uri = null) { }
    public void NotifyProxyError(string clusterId, string? destinationId, string errorMessage) { }
    public void NotifyRateLimitExceeded(string clientIp, string? routeId = null) { }
    public void NotifyConfigChange(string eventType, string target, string? operatorName = null, object? details = null) { }
    public void NotifyCustom(string eventType, string title, string message) { }
}
