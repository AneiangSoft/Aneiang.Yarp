using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Services;

public interface INotificationService
{
    Task PreloadAsync(CancellationToken ct = default);
    void InvalidateCache();
    Task NotifyAsync(NotificationEvent evt, CancellationToken ct = default);
    Task<bool> TestChannelAsync(string channelId, CancellationToken ct = default);
    void NotifyCircuitBreakerOpen(string clusterId, string? destinationId = null);
    void NotifyRetryExhausted(string clusterId, string routeId, int attempts, int statusCode);
    void NotifyWafBlock(string clientIp, string blockReason, string? uri = null);
    void NotifyProxyError(string clusterId, string? destinationId, string errorMessage);
    void NotifyRateLimitExceeded(string clientIp, string? routeId = null);
    void NotifyConfigChange(string eventType, string target, string? operatorName = null, object? details = null);
    void NotifyCustom(string eventType, string title, string message);
}
