using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// AI assistant API: SSE streaming chat, config management, session history.
/// </summary>
public class AIController : Controller
{
    private readonly AIService _aiService;
    private readonly AIConfigStore _configStore;
    private readonly IAIConversationRepository _conversationRepo;
    private readonly AIFunctionService _functionService;

    public AIController(
        AIService aiService,
        AIConfigStore configStore,
        IAIConversationRepository conversationRepo,
        AIFunctionService functionService)
    {
        _aiService = aiService;
        _configStore = configStore;
        _conversationRepo = conversationRepo;
        _functionService = functionService;
    }

    /// <summary>Get AI configuration (API key masked).</summary>
    [HttpGet("api/ai/config")]
    public IActionResult GetConfig()
    {
        var opts = _configStore.Current;
        // Mask the API key for security
        var maskedKey = string.IsNullOrEmpty(opts.ApiKey)
            ? ""
            : opts.ApiKey.Length <= 8
                ? new string('*', opts.ApiKey.Length)
                : opts.ApiKey[..4] + "****" + opts.ApiKey[^4..];
        return Json(new
        {
            enabled = opts.Enabled,
            provider = opts.Provider,
            apiKey = maskedKey,
            hasApiKey = !string.IsNullOrWhiteSpace(opts.ApiKey),
            baseUrl = opts.BaseUrl,
            chatModel = opts.ChatModel,
            maxTokens = opts.MaxTokens,
            temperature = opts.Temperature,
            maxHistory = opts.MaxHistory,
            reasoningEffort = opts.ReasoningEffort,
            useCache = opts.UseCache,
            enhanceNotif = opts.EnhanceNotif,
            fallback = new
            {
                enabled = opts.Fallback.Enabled,
                provider = opts.Fallback.Provider,
                hasApiKey = !string.IsNullOrWhiteSpace(opts.Fallback.ApiKey),
                baseUrl = opts.Fallback.BaseUrl,
                model = opts.Fallback.Model
            },
            mcpServer = new { enabled = opts.McpServer.Enabled, port = opts.McpServer.Port },
            isConfigured = opts.IsConfigured
        });
    }

    /// <summary>Save AI configuration.</summary>
    [HttpPost("api/ai/config")]
    public IActionResult SaveConfig([FromBody] AIConfigSaveRequest request)
    {
        var current = _configStore.Current;
        var updated = new AIOptions
        {
            Enabled = request.Enabled ?? current.Enabled,
            Provider = request.Provider ?? current.Provider,
            ApiKey = string.IsNullOrEmpty(request.ApiKey) ? current.ApiKey : request.ApiKey,
            BaseUrl = request.BaseUrl ?? current.BaseUrl,
            ChatModel = request.ChatModel ?? current.ChatModel,
            MaxTokens = request.MaxTokens ?? current.MaxTokens,
            Temperature = request.Temperature ?? current.Temperature,
            MaxHistory = request.MaxHistory ?? current.MaxHistory,
            ReasoningEffort = request.ReasoningEffort ?? current.ReasoningEffort,
            UseCache = request.UseCache ?? current.UseCache,
            EnhanceNotif = request.EnhanceNotif ?? current.EnhanceNotif,
            Fallback = new AIFallbackOptions
            {
                Enabled = request.Fallback?.Enabled ?? current.Fallback.Enabled,
                Provider = request.Fallback?.Provider ?? current.Fallback.Provider,
                ApiKey = string.IsNullOrEmpty(request.Fallback?.ApiKey) ? current.Fallback.ApiKey : request.Fallback.ApiKey,
                BaseUrl = request.Fallback?.BaseUrl ?? current.Fallback.BaseUrl,
                Model = request.Fallback?.Model ?? current.Fallback.Model
            },
            McpServer = new McpServerOptions
            {
                Enabled = request.McpServer?.Enabled ?? current.McpServer.Enabled,
                Port = request.McpServer?.Port ?? current.McpServer.Port
            }
        };
        _configStore.Save(updated);
        return Json(new { code = 200, message = "AI configuration saved" });
    }

    /// <summary>Test the LLM API connection.</summary>
    [HttpPost("api/ai/test")]
    public async Task<IActionResult> TestConnection()
    {
        if (!_configStore.IsConfigured)
            return Json(new { ok = false, message = "AI is not configured. Please set API Key and model." });
        var (ok, message) = await _aiService.TestConnectionAsync();
        return Json(new { ok, message });
    }

    /// <summary>Stream a chat completion via SSE.</summary>
    [HttpPost("api/ai/chat")]
    public async Task Chat([FromBody] AIChatRequest request)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();

        var ct = HttpContext.RequestAborted;
        var sessionId = string.IsNullOrEmpty(request.SessionId) ? Guid.NewGuid().ToString("N") : request.SessionId;
        var message = request.Message ?? "";

        if (string.IsNullOrWhiteSpace(message))
        {
            await WriteSSEAsync(new { type = "error", error = "Message is empty" });
            await WriteSSEAsync(new { type = "done", sessionId });
            return;
        }

        // Send sessionId back so frontend can track the session
        await WriteSSEAsync(new { type = "session", sessionId });

        try
        {
            await foreach (var evt in _aiService.StreamChatAsync(sessionId, message, ct))
            {
                await WriteSSEAsync(evt);
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            await WriteSSEAsync(new { type = "error", error = ex.Message });
        }

        await WriteSSEAsync(new { type = "done", sessionId });
    }

    /// <summary>List all AI chat sessions.</summary>
    [HttpGet("api/ai/sessions")]
    public async Task<IActionResult> ListSessions()
    {
        var sessions = await _conversationRepo.ListSessionsAsync(50);
        return Json(sessions);
    }

    /// <summary>Delete a chat session and all its messages.</summary>
    [HttpDelete("api/ai/sessions/{sessionId}")]
    public async Task<IActionResult> DeleteSession(string sessionId)
    {
        await _conversationRepo.DeleteSessionAsync(sessionId);
        return Json(new { code = 200, message = "Session deleted" });
    }

    /// <summary>Execute a confirmed write tool (toggle/create/delete route or cluster).</summary>
    [HttpPost("api/ai/tool/execute")]
    public async Task<IActionResult> ExecuteTool([FromBody] AIExecuteToolRequest request)
    {
        var toolName = request.ToolName ?? "";
        JsonElement? args = string.IsNullOrWhiteSpace(request.Arguments)
            ? null
            : JsonSerializer.Deserialize<JsonElement>(request.Arguments);
        var result = await _functionService.ExecuteWriteToolAsync(toolName, args);
        return Json(new { ok = !result.Contains("\"error\""), result });
    }

    /// <summary>Get messages from a specific chat session.</summary>
    [HttpGet("api/ai/sessions/{sessionId}/messages")]
    public async Task<IActionResult> GetSessionMessages(string sessionId)
    {
        var messages = await _conversationRepo.GetSessionMessagesAsync(sessionId, 50);
        return Json(messages);
    }

    private static readonly JsonSerializerOptions SSEJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private async Task WriteSSEAsync(object data)
    {
        var json = JsonSerializer.Serialize(data, SSEJsonOptions);
        await Response.WriteAsync($"data: {json}\n\n");
        await Response.Body.FlushAsync();
    }
}

/// <summary>Chat request from the frontend.</summary>
public class AIChatRequest
{
    public string? SessionId { get; set; }
    public string? Message { get; set; }
}

/// <summary>Write-tool execution request (sent after user confirmation).</summary>
public class AIExecuteToolRequest
{
    public string? ToolName { get; set; }
    public string? Arguments { get; set; }
}

/// <summary>Config save request (all fields optional, null = keep current).</summary>
public class AIConfigSaveRequest
{
    public bool? Enabled { get; set; }
    public string? Provider { get; set; }
    public string? ApiKey { get; set; } // null = keep existing, "" = clear
    public string? BaseUrl { get; set; }
    public string? ChatModel { get; set; }
    public int? MaxTokens { get; set; }
    public double? Temperature { get; set; }
    public int? MaxHistory { get; set; }
    public string? ReasoningEffort { get; set; }
    public bool? UseCache { get; set; }
    public bool? EnhanceNotif { get; set; }
    public AIFallbackSaveRequest? Fallback { get; set; }
    public McpServerSaveRequest? McpServer { get; set; }
}

public class AIFallbackSaveRequest
{
    public bool Enabled { get; set; }
    public string? Provider { get; set; }
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
}

public class McpServerSaveRequest
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 8090;
}
