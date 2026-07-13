using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>
/// Runtime AI settings service. Allows reading/updating AI options
/// without requiring application restart. Persists to SQLite global settings.
/// </summary>
public class AISettingsService
{
    private readonly AIOptions _options;
    private readonly ILogger<AISettingsService> _logger;

    public AISettingsService(IOptions<AIOptions> options, ILogger<AISettingsService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Returns current AI settings (masked API key).</summary>
    public AISettingsDto GetSettings()
    {
        return new AISettingsDto
        {
            Enabled = _options.Enabled,
            Provider = _options.Provider,
            ApiKey = MaskKey(_options.ApiKey),
            BaseUrl = _options.BaseUrl,
            ChatModel = _options.ChatModel,
            AnalysisModel = _options.AnalysisModel,
            MaxTokens = _options.MaxTokens,
            Temperature = _options.Temperature,
            MaxConversationHistory = _options.MaxConversationHistory,
            EnableBackgroundAnalysis = _options.EnableBackgroundAnalysis,
            EnhanceNotifications = _options.EnhanceNotifications,
            IsConfigured = !string.IsNullOrWhiteSpace(_options.ApiKey)
        };
    }

    /// <summary>Update AI settings at runtime.</summary>
    public void UpdateSettings(AISettingsDto dto)
    {
        _options.Enabled = dto.Enabled;
        _options.Provider = dto.Provider ?? "openai";
        _options.BaseUrl = dto.BaseUrl ?? "https://api.openai.com/v1";
        _options.ChatModel = dto.ChatModel ?? "gpt-4o-mini";
        _options.AnalysisModel = dto.AnalysisModel ?? "gpt-4o-mini";
        _options.MaxTokens = dto.MaxTokens;
        _options.Temperature = dto.Temperature;
        _options.MaxConversationHistory = dto.MaxConversationHistory;
        _options.EnableBackgroundAnalysis = dto.EnableBackgroundAnalysis;
        _options.EnhanceNotifications = dto.EnhanceNotifications;

        // Only update API key if a new (non-masked) value is provided
        if (!string.IsNullOrWhiteSpace(dto.ApiKey) && !dto.ApiKey.Contains("****"))
        {
            _options.ApiKey = dto.ApiKey;
        }

        // Apply provider presets
        ApplyProviderPreset(_options.Provider, _options);

        _logger.LogInformation("AI settings updated: Provider={Provider}, Model={Model}, Enabled={Enabled}",
            _options.Provider, _options.ChatModel, _options.Enabled);
    }

    private static void ApplyProviderPreset(string provider, AIOptions options)
    {
        switch (provider.ToLowerInvariant())
        {
            case "deepseek":
                if (options.BaseUrl.Contains("openai.com"))
                    options.BaseUrl = "https://api.deepseek.com/v1";
                if (options.ChatModel.StartsWith("gpt-"))
                    options.ChatModel = "deepseek-chat";
                break;
            case "qwen":
                if (options.BaseUrl.Contains("openai.com"))
                    options.BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1";
                if (options.ChatModel.StartsWith("gpt-"))
                    options.ChatModel = "qwen-turbo";
                break;
        }
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Length <= 8) return "****";
        return key[..4] + "****" + key[^4..];
    }
}

/// <summary>DTO for AI settings API.</summary>
public class AISettingsDto
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "openai";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public string AnalysisModel { get; set; } = "gpt-4o-mini";
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.3;
    public int MaxConversationHistory { get; set; } = 20;
    public bool EnableBackgroundAnalysis { get; set; }
    public bool EnhanceNotifications { get; set; }
    public bool IsConfigured { get; set; }
}
