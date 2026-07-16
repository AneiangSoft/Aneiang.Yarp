namespace Aneiang.Yarp.Dashboard.Modules.Policy.Models;

/// <summary>
/// Route policy template: contains retry, rate-limit, WAF toggle.
/// Can be applied to one or more routes via route metadata.
/// </summary>
public class RoutePolicy
{
    /// <summary>Internal immutable policy UID.</summary>
    public string PolicyUid { get; set; } = string.Empty;

    /// <summary>Unique identifier for this policy. Kept for compatibility; prefer PolicyKey for new code.</summary>
    public string PolicyId { get; set; } = string.Empty;

    /// <summary>Policy key alias.</summary>
    public string PolicyKey
    {
        get => PolicyId;
        set => PolicyId = value;
    }

    /// <summary>Display name of this policy.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Description of what this policy does.</summary>
    public string? Description { get; set; }

    /// <summary>Is this policy enabled. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When this policy was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Route IDs this policy is applied to (read-only, maintained by system).</summary>
    public List<string> AppliedRoutes { get; set; } = new();

    /// <summary>Retry settings for this policy.</summary>
    public PolicyRetry? Retry { get; set; }

    /// <summary>Rate limit settings for this policy.</summary>
    public PolicyRateLimit? RateLimit { get; set; }

    /// <summary>WAF route-level toggle: true=force on, false=force off, null=follow global default.</summary>
    public bool? WafEnabled { get; set; }

    /// <summary>Generate metadata entries for route configuration.</summary>
    public Dictionary<string, string> ToMetadata()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Retry != null)
        {
            foreach (var kvp in Retry.ToMetadata())
                metadata[kvp.Key] = kvp.Value;
        }

        if (RateLimit != null)
        {
            foreach (var kvp in RateLimit.ToMetadata())
                metadata[kvp.Key] = kvp.Value;
        }

        if (WafEnabled.HasValue)
        {
            metadata["Waf:Enabled"] = WafEnabled.Value.ToString().ToLowerInvariant();
        }

        metadata["Policy:Id"] = PolicyId;
        metadata["Policy:Name"] = DisplayName;

        return metadata;
    }
}


