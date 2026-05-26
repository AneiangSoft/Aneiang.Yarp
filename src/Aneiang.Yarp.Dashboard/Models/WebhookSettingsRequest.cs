namespace Aneiang.Yarp.Dashboard.Models;

/// <summary>
/// Request body for updating webhook notification settings.
/// Supports per-platform endpoint lists (URL + Secret pairs).
/// </summary>
public class WebhookSettingsRequest
{
    /// <summary>Platform-specific webhook configurations.</summary>
    public Dictionary<string, WebhookPlatformEntry>? Platforms { get; set; }
}

/// <summary>
/// Per-platform webhook configuration entry.
/// </summary>
public class WebhookPlatformEntry
{
    /// <summary>List of webhook endpoints (URL + Secret pairs) for this platform.</summary>
    public List<WebhookEndpointDto>? Endpoints { get; set; }
}

/// <summary>
/// DTO for a single webhook endpoint with URL and optional secret.
/// </summary>
public class WebhookEndpointDto
{
    /// <summary>Webhook URL.</summary>
    public string? Url { get; set; }

    /// <summary>Optional signing secret for this endpoint.</summary>
    public string? Secret { get; set; }
}
