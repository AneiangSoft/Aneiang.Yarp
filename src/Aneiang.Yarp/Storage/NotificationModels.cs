namespace Aneiang.Yarp.Storage;

/// <summary>
/// Notification channel types supported by the system.
/// </summary>
public enum NotificationChannelType
{
    /// <summary>DingTalk group robot webhook.</summary>
    DingTalk = 0,

    /// <summary>Generic HTTP webhook (supports any service that accepts POST).</summary>
    Generic = 1
}

/// <summary>
/// Severity level for notifications.
/// </summary>
public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

/// <summary>
/// Represents a notification channel endpoint configuration.
/// </summary>
public class NotificationChannel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public NotificationChannelType Type { get; set; } = NotificationChannelType.Generic;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Secret { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Notification rule that defines when and how to send notifications.
/// </summary>
public class NotificationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = string.Empty;

    /// <summary>
/// Event types this rule applies to. Null/empty means all events.
/// Examples: "CircuitBreakerOpen", "RetryExhausted", "WafBlock", "ProxyError", "RateLimitExceeded"
/// </summary>
    public List<string> EventTypes { get; set; } = [];

    /// <summary>
/// Minimum severity level for this rule.
/// </summary>
    public NotificationSeverity MinSeverity { get; set; } = NotificationSeverity.Info;

    /// <summary>
/// IDs of channels to notify when rule matches.
/// </summary>
    public List<string> ChannelIds { get; set; } = [];

    /// <summary>
/// Whether this rule is enabled.
/// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
/// Cooldown period in seconds between notifications for the same source.
/// Prevents notification spam.
/// </summary>
    public int CooldownSeconds { get; set; } = 300;

    /// <summary>
/// Whether to include alert history (record the notification in history).
/// </summary>
    public bool RecordToHistory { get; set; } = true;

    /// <summary>
/// Route/cluster filter. If not empty, only events matching these targets trigger notifications.
/// </summary>
    public List<string>? TargetRouteIds { get; set; }
    public List<string>? TargetClusterIds { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A notification event sent to channels.
/// </summary>
public class NotificationEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Source cluster if applicable.</summary>
    public string? ClusterId { get; set; }

    /// <summary>Source route if applicable.</summary>
    public string? RouteId { get; set; }

    /// <summary>Client IP if applicable.</summary>
    public string? ClientIp { get; set; }

    /// <summary>Operator who triggered this event (e.g., "dashboard-user").</summary>
    public string? Operator { get; set; }

    /// <summary>Additional metadata specific to event type.</summary>
    public Dictionary<string, string?> Metadata { get; set; } = new();
}

/// <summary>
/// Notification history record for the dashboard.
/// </summary>
public class NotificationHistory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EventType { get; set; } = string.Empty;
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? ClusterId { get; set; }
    public string? ClusterUid { get; set; }
    public string? ClusterKeySnapshot { get; set; }
    public string? RouteId { get; set; }
    public string? RouteUid { get; set; }
    public string? RouteKeySnapshot { get; set; }
    public string? ClientIp { get; set; }
    public string? BlockReason { get; set; }
    public string? RequestUri { get; set; }
    public string? ErrorMessage { get; set; }
    public int? AttemptCount { get; set; }
    public int? LastStatusCode { get; set; }

    /// <summary>Channels that were notified for this event.</summary>
    public List<string> NotifiedChannels { get; set; } = [];

    /// <summary>Whether this notification was sent successfully.</summary>
    public bool DeliverySuccess { get; set; } = true;
}
