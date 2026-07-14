namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>
/// Configuration options for the AI module.
/// Bound from <c>Gateway:Dashboard:AI</c> configuration section.
/// </summary>
public class AIOptions
{
    public const string SectionName = "Gateway:Dashboard:AI";

    /// <summary>Master switch — when false, no AI services are registered.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Provider identifier: openai / deepseek / qwen / custom.</summary>
    public string Provider { get; set; } = "openai";

    /// <summary>API key for the LLM provider.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Base URL for the OpenAI-compatible API endpoint.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Model used for interactive chat conversations.</summary>
    public string ChatModel { get; set; } = "gpt-4o-mini";

    /// <summary>Model used for background analysis tasks (can be cheaper).</summary>
    public string AnalysisModel { get; set; } = "gpt-4o-mini";

    /// <summary>Maximum tokens per response.</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>Sampling temperature (0.0 = deterministic, 1.0 = creative).</summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>How many recent messages to include as conversation context.</summary>
    public int MaxConversationHistory { get; set; } = 20;

    /// <summary>Enable background periodic log analysis.</summary>
    public bool EnableBackgroundAnalysis { get; set; } = false;

    /// <summary>Interval between background analysis runs.</summary>
    public TimeSpan AnalysisInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Enable AI-enhanced notifications (adds context to alerts).</summary>
    public bool EnhanceNotifications { get; set; } = false;

    /// <summary>Minimum seconds between AI-enhanced notification calls.</summary>
    public int NotificationEnhanceCooldownSeconds { get; set; } = 60;

    /// <summary>
    /// Allow the "custom" provider option in the Dashboard UI.
    /// When true (default), users can select "Custom" in the provider dropdown
    /// and enter any OpenAI-compatible API endpoint (BaseUrl).
    /// All user-supplied URLs are validated against SSRF patterns before acceptance:
    ///   - HTTP only allowed for loopback (local LLM: Ollama, LM Studio, vLLM).
    ///   - HTTPS blocks private/reserved IP ranges.
    ///   - Cloud metadata service hostnames are always blocked.
    /// Set to false in appsettings.json to restrict to built-in providers only.
    /// This setting is ONLY configurable via appsettings.json —
    /// the runtime API cannot change it.
    /// </summary>
    public bool AllowCustomProvider { get; set; } = true;
}
