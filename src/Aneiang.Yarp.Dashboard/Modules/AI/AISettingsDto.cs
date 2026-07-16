namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>DTO for AI settings API.</summary>
public class AISettingsDto
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "openai";
    public string? ApiKey { get; set; } = "";

    /// <summary>
    /// Base URL for the LLM API endpoint.
    /// User-editable for all providers (supports API proxies / mirrors).
    /// Subject to SSRF validation on save — invalid URLs are rejected.
    /// </summary>
    public string? BaseUrl { get; set; } = "https://api.openai.com/v1";

    public string ChatModel { get; set; } = "gpt-4o-mini";
    public string AnalysisModel { get; set; } = "gpt-4o-mini";
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.3;
    public int MaxConversationHistory { get; set; } = 20;
    public bool EnableBackgroundAnalysis { get; set; }
    public bool EnhanceNotifications { get; set; }
    public bool IsConfigured { get; set; }

    /// <summary>
    /// Whether the "custom" provider option is available in the Dashboard.
    /// Read-only — controlled by AllowCustomProvider in appsettings.json.
    /// </summary>
    public bool AllowCustomProvider { get; set; }
}
