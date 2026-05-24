namespace Aneiang.Yarp.Dashboard.Models;

/// <summary>
/// Request body for updating webhook notification settings.
/// Supports per-platform URL lists and secrets.
/// </summary>
public class WebhookSettingsRequest
{
    /// <summary>Platform-specific webhook configurations.</summary>
    public Dictionary<string, WebhookPlatformEntry>? Platforms { get; set; }

    /// <summary>
    /// Generic signing secret (backward compat).
    /// Set to null to keep existing secret unchanged.
    /// Set to empty string to clear the secret.
    /// </summary>
    public string? WebhookSecret { get; set; }
}

/// <summary>
/// Per-platform webhook configuration entry.
/// </summary>
public class WebhookPlatformEntry
{
    /// <summary>List of webhook URLs for this platform.</summary>
    public List<string>? Urls { get; set; }

    /// <summary>
    /// Platform-specific signing secret.
    /// Set to null to keep existing secret unchanged.
    /// Set to empty string to clear the secret.
    /// </summary>
    public string? Secret { get; set; }
}
