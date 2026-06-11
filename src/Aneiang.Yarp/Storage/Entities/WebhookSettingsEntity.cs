namespace Aneiang.Yarp.Storage;

/// <summary>Webhook Settings entity for database storage.</summary>
public class WebhookSettingsEntity
{
    public bool Enabled { get; set; }
    public string? Endpoints { get; set; } // JSON array
    public string? Events { get; set; } // JSON array
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public string? Secret { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
