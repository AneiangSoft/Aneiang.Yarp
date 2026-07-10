using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Interface for the notification service. Defined in the core library so that
/// non-Dashboard components (e.g. Aneiang.Yarp.Client) can trigger notifications
/// without depending on the Dashboard assembly.
/// </summary>
public interface INotificationService
{
    /// <summary>Preload settings into memory cache.</summary>
    Task PreloadAsync(CancellationToken ct = default);

    /// <summary>Invalidate the settings cache.</summary>
    void InvalidateCache();

    /// <summary>Send a notification event through all matching rules.</summary>
    Task NotifyAsync(NotificationEvent evt, CancellationToken ct = default);

    /// <summary>Send a test notification to a specific channel.</summary>
    Task<bool> TestChannelAsync(string channelId, CancellationToken ct = default);

    /// <summary>Notify circuit breaker open event.</summary>
    void NotifyCircuitBreakerOpen(string clusterId, string? destinationId = null);

    /// <summary>Notify retry exhausted event.</summary>
    void NotifyRetryExhausted(string clusterId, string routeId, int attempts, int statusCode);

    /// <summary>Notify WAF block event.</summary>
    void NotifyWafBlock(string clientIp, string blockReason, string? uri = null);

    /// <summary>Notify proxy error event.</summary>
    void NotifyProxyError(string clusterId, string? destinationId, string errorMessage);

    /// <summary>Notify rate limit exceeded event.</summary>
    void NotifyRateLimitExceeded(string clientIp, string? routeId = null);

    /// <summary>Notify config change event (AddRoute, UpdateRoute, RemoveRoute, etc.).</summary>
    void NotifyConfigChange(string eventType, string target, string? operatorName = null, object? details = null);

    /// <summary>Send a custom notification.</summary>
    void NotifyCustom(string eventType, string title, string message);
}

/// <summary>
/// No-op notification service used when <see cref="INotificationService"/> is not available.
/// All methods are no-ops; callers should use the null-conditional pattern (e.g.,
/// <c>notificationService ?? NullNotificationService.Instance</c>).
/// </summary>
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
