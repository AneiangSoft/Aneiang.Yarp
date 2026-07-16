namespace Aneiang.Yarp.Storage;

public class NotificationSettingsEntity
{
    public string Id { get; set; } = "notification_settings";

    public bool Enabled { get; set; } = true;

    public string? Channels { get; set; }

    public string? Rules { get; set; }

    public string? GlobalSettings { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
