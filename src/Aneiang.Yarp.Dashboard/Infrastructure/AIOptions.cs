namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// AI assistant configuration. Binds from <c>AI</c> config section.
/// </summary>
public class AIOptions
{
    public const string SectionName = "AI";

    public bool Enabled { get; set; }
    public string Provider { get; set; } = "deepseek";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1";
    public string ChatModel { get; set; } = "deepseek-v4-flash";
    public string AnalysisModel { get; set; } = "deepseek-v4-flash";
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.7;
    public int MaxHistory { get; set; } = 20;
    public string ReasoningEffort { get; set; } = "auto";
    public bool UseCache { get; set; } = true;
    public bool BgAnalysis { get; set; } = false;
    public bool EnhanceNotif { get; set; } = false;
    public AIFallbackOptions Fallback { get; set; } = new();
    public McpServerOptions McpServer { get; set; } = new();

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ChatModel);
}

public class AIFallbackOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
}

public class McpServerOptions
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 8090;
}

/// <summary>Provider preset metadata for the settings UI dropdown.</summary>
public static class AIProviderPresets
{
    public static readonly Dictionary<string, (string BaseUrl, string DefaultModel, string Label)> Presets = new()
    {
        ["deepseek"] = ("https://api.deepseek.com/v1", "deepseek-v4-flash", "DeepSeek"),
        ["openai"] = ("https://api.openai.com/v1", "gpt-5.6-luna", "OpenAI"),
        ["anthropic"] = ("https://api.anthropic.com/v1", "claude-opus-5", "Anthropic"),
        ["google"] = ("https://generativelanguage.googleapis.com/v1", "gemini-3.1-pro", "Google"),
        ["xai"] = ("https://api.x.ai/v1", "grok-4.5", "xAI"),
        ["glm"] = ("https://open.bigmodel.cn/api/paas/v4", "glm-5.2", "GLM (智谱)"),
        ["qwen"] = ("https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen3.8-max", "通义千问"),
        ["custom"] = ("", "", "自定义"),
    };

    public static (string BaseUrl, string DefaultModel, string Label) Get(string provider)
        => Presets.TryGetValue(provider, out var p) ? p : Presets["custom"];
}
