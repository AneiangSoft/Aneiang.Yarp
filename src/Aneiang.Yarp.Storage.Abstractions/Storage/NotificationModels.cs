namespace Aneiang.Yarp.Storage;

public enum NotificationChannelType
{
    DingTalk = 0,

    Generic = 1
}

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public class NotificationChannel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public NotificationChannelType Type { get; set; } = NotificationChannelType.Generic;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Secret { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public class NotificationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = string.Empty;

    public List<string> EventTypes { get; set; } = [];

    public NotificationSeverity MinSeverity { get; set; } = NotificationSeverity.Info;

    public List<string> ChannelIds { get; set; } = [];

    public bool Enabled { get; set; } = true;

    public int CooldownSeconds { get; set; } = 300;

    public bool RecordToHistory { get; set; } = true;

    public List<string>? TargetRouteIds { get; set; }
    public List<string>? TargetClusterIds { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public class NotificationEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string? ClusterId { get; set; }

    public string? RouteId { get; set; }

    public string? ClientIp { get; set; }

    public string? Operator { get; set; }

    public Dictionary<string, string?> Metadata { get; set; } = new();
}

public class NotificationHistory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EventType { get; set; } = string.Empty;
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
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

    public List<string> NotifiedChannels { get; set; } = [];

    public bool DeliverySuccess { get; set; } = true;
}
