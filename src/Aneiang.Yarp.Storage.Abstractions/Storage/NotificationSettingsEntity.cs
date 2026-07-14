namespace Aneiang.Yarp.Storage;

/// <summary>
/// Notification settings entity for database storage.
/// Stores channels, rules, and global settings as JSON.
/// </summary>
public class NotificationSettingsEntity
{
    /// <summary>Primary key. Single row: always "notification_settings".</summary>
    public string Id { get; set; } = "notification_settings";

    /// <summary>Global notification enabled state.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>JSON array of notification channels.</summary>
    public string? Channels { get; set; }

    /// <summary>JSON array of notification rules.</summary>
    public string? Rules { get; set; }

    /// <summary>JSON object of global settings.</summary>
    public string? GlobalSettings { get; set; }

    /// <summary>When this record was last updated.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
