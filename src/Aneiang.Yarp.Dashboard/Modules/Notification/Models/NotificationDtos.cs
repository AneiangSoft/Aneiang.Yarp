using System.Text.Json.Serialization;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Models;

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public class SaveNotificationSettingsRequest
{
    [JsonPropertyName("channels")]
    public List<ChannelRequest>? Channels { get; set; }

    [JsonPropertyName("rules")]
    public List<RuleRequest>? Rules { get; set; }

    [JsonPropertyName("globalSettings")]
    public GlobalSettingsRequest? GlobalSettings { get; set; }
}

public class ChannelRequest
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "Generic";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("secret")]
    public string? Secret { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public class RuleRequest
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("eventTypes")]
    public List<string>? EventTypes { get; set; }

    [JsonPropertyName("channelIds")]
    public List<string> ChannelIds { get; set; } = [];

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("cooldownSeconds")]
    public int CooldownSeconds { get; set; } = 300;

    [JsonPropertyName("recordToHistory")]
    public bool RecordToHistory { get; set; } = true;

    [JsonPropertyName("targetRouteIds")]
    public List<string>? TargetRouteIds { get; set; }

    [JsonPropertyName("targetClusterIds")]
    public List<string>? TargetClusterIds { get; set; }
}

public class GlobalSettingsRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("maxHistoryRecords")]
    public int MaxHistoryRecords { get; set; } = 500;

    [JsonPropertyName("defaultTimeoutSeconds")]
    public int DefaultTimeoutSeconds { get; set; } = 10;

    [JsonPropertyName("defaultRetryCount")]
    public int DefaultRetryCount { get; set; } = 1;
}

// ─── Response DTOs ────────────────────────────────────────────────────────────

public class NotificationSettingsResponse
{
    [JsonPropertyName("channels")]
    public List<ChannelResponse> Channels { get; set; } = [];

    [JsonPropertyName("rules")]
    public List<RuleResponse> Rules { get; set; } = [];

    [JsonPropertyName("globalSettings")]
    public GlobalSettingsResponse GlobalSettings { get; set; } = new();
}

public class ChannelResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "Generic";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("hasSecret")]
    public bool HasSecret { get; set; }

    /// <summary>Secret value if one is configured, otherwise null.
    /// Returned only when editing so the field can be preserved on save.</summary>
    [JsonPropertyName("secret")]
    public string? Secret { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public class RuleResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("eventTypes")]
    public List<string> EventTypes { get; set; } = [];

    [JsonPropertyName("channelIds")]
    public List<string> ChannelIds { get; set; } = [];

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("cooldownSeconds")]
    public int CooldownSeconds { get; set; }

    [JsonPropertyName("recordToHistory")]
    public bool RecordToHistory { get; set; }

    [JsonPropertyName("targetRouteIds")]
    public List<string>? TargetRouteIds { get; set; }

    [JsonPropertyName("targetClusterIds")]
    public List<string>? TargetClusterIds { get; set; }

    [JsonPropertyName("channels")]
    public List<ChannelResponse>? ChannelDetails { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public class GlobalSettingsResponse
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("maxHistoryRecords")]
    public int MaxHistoryRecords { get; set; } = 500;

    [JsonPropertyName("defaultTimeoutSeconds")]
    public int DefaultTimeoutSeconds { get; set; } = 10;

    [JsonPropertyName("defaultRetryCount")]
    public int DefaultRetryCount { get; set; } = 1;
}

public class NotificationHistoryResponse
{
    [JsonPropertyName("entries")]
    public List<NotificationHistory> Entries { get; set; } = [];

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; } = 100;
}

public class NotificationSummaryResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("unread")]
    public int Unread { get; set; }

    [JsonPropertyName("byEventType")]
    public Dictionary<string, int> ByEventType { get; set; } = new();

    [JsonPropertyName("lastEvent")]
    public NotificationHistory? LastEvent { get; set; }

    [JsonPropertyName("channelsConfigured")]
    public int ChannelsConfigured { get; set; }

    [JsonPropertyName("rulesActive")]
    public int RulesActive { get; set; }
}
