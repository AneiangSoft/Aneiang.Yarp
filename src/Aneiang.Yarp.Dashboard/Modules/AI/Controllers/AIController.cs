using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.AI;
using Aneiang.Yarp.Dashboard.Modules.AI.Services;
using Aneiang.Yarp.Dashboard.Modules.AI.Tools;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Controllers;

/// <summary>
/// AI module API endpoints: settings, chat with tool calling, action confirmation, and analysis.
/// </summary>
[Route("api/ai")]
[ApiController]
public class AIController : ControllerBase
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAIProvider _provider;
    private readonly AISettingsService _settingsService;
    private readonly ChatService _chatService;
    private readonly IAIConversationRepository _conversationRepo;
    private readonly IAIAnalysisRepository _analysisRepo;
    private readonly GatewayContextProvider _contextProvider;
    private readonly GatewayToolRegistry _toolRegistry;
    private readonly GatewayToolExecutor _toolExecutor;
    private readonly AIOptions _options;
    private readonly string _locale;

    public AIController(
        IAIProvider provider,
        AISettingsService settingsService,
        ChatService chatService,
        IAIConversationRepository conversationRepo,
        IAIAnalysisRepository analysisRepo,
        GatewayContextProvider contextProvider,
        GatewayToolRegistry toolRegistry,
        GatewayToolExecutor toolExecutor,
        IOptions<AIOptions> options,
        IOptions<DashboardOptions> dashboardOptions)
    {
        _provider = provider;
        _settingsService = settingsService;
        _chatService = chatService;
        _conversationRepo = conversationRepo;
        _analysisRepo = analysisRepo;
        _contextProvider = contextProvider;
        _toolRegistry = toolRegistry;
        _toolExecutor = toolExecutor;
        _options = options.Value;
        _locale = dashboardOptions.Value.Locale ?? "zh-CN";
    }

    private bool IsChinese => _locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>Get AI module status and current settings.</summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var settings = _settingsService.GetSettings();
        return Ok(new
        {
            code = 200,
            data = new
            {
                enabled = settings.Enabled,
                isConfigured = settings.IsConfigured,
                provider = settings.Provider,
                providerName = _provider.ProviderName,
                available = _provider.IsAvailable,
                chatModel = settings.ChatModel,
                analysisModel = settings.AnalysisModel,
                backgroundAnalysis = settings.EnableBackgroundAnalysis,
                enhanceNotifications = settings.EnhanceNotifications,
                toolsEnabled = _toolRegistry.GetToolDefinitions().Count,
                allowCustomProvider = settings.AllowCustomProvider
            }
        });
    }

    /// <summary>Get full AI settings (for settings page).</summary>
    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        return Ok(new { code = 200, data = _settingsService.GetSettings() });
    }

    /// <summary>Update AI settings.</summary>
    [HttpPut("settings")]
    public IActionResult UpdateSettings([FromBody] AISettingsDto dto)
    {
        _settingsService.UpdateSettings(dto);
        return Ok(new { code = 200, message = "AI settings updated" });
    }

    /// <summary>Get available tools for the AI assistant.</summary>
    [HttpGet("tools")]
    public IActionResult GetTools()
    {
        var tools = _toolRegistry.GetToolDefinitions();
        return Ok(new
        {
            code = 200,
            data = tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                isReadOnly = t.IsReadOnly
            })
        });
    }

    /// <summary>
    /// Test AI connection with a simple non-streaming request.
    /// Returns success only if the API responds without errors.
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> TestConnection(CancellationToken ct)
    {
        if (!_provider.IsAvailable)
            return StatusCode(503, new { code = 503, success = false, error = "AI provider not configured" });

        try
        {
            var request = new AIChatRequest
            {
                Messages = [new AIChatMessage { Role = "user", Content = "Say OK" }],
                MaxTokens = 10,
                Temperature = 0
            };
            var response = await _provider.ChatAsync(request, ct);
            return Ok(new
            {
                code = 200,
                success = true,
                message = $"Connected to {_provider.ProviderName}",
                tokensUsed = response.PromptTokens + response.CompletionTokens
            });
        }
        catch (Exception ex)
        {
            return Ok(new { code = 200, success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Chat endpoint with tool calling support.
    /// Uses non-streaming for tool call loop, then streams the final text response via SSE.
    /// Write operations return a pending_action event for user confirmation.
    /// </summary>
    [HttpPost("chat")]
    public async Task Chat([FromBody] ChatRequestDto request, CancellationToken ct)
    {
        if (!_provider.IsAvailable)
        {
            Response.StatusCode = 503;
            await Response.WriteAsync("{\"error\":\"AI provider not configured\"}", ct);
            return;
        }

        var userMessage = request.Messages?.LastOrDefault();
        if (userMessage == null || string.IsNullOrWhiteSpace(userMessage.Content))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("{\"error\":\"No message provided\"}", ct);
            return;
        }

        // Save user message and get session ID
        var sessionId = await _chatService.ProcessMessageAsync(request.SessionId, userMessage.Content, ct);
        var locale = request.Locale ?? _locale;

        // Build the AI request with full conversation history + gateway context + tools
        var aiRequest = await _chatService.BuildChatRequestAsync(sessionId, locale, ct);

        SseHelper.SetupResponse(Response, sessionId);

        try
        {
            // Phase 1: Tool calling loop (non-streaming for tool accuracy)
            var result = await _chatService.ProcessWithToolsAsync(sessionId, aiRequest, locale, ct);

            switch (result.Type)
            {
                case ChatResultType.Text:
                    // Phase 2: Stream the final response in real-time via SSE
                    var streamRequest = new AIChatRequest
                    {
                        SystemPrompt = aiRequest.SystemPrompt,
                        Messages = result.AccumulatedMessages ?? aiRequest.Messages,
                        Tools = null,
                        Model = aiRequest.Model,
                        Temperature = 0.3,
                        MaxTokens = aiRequest.MaxTokens
                    };

                    var fullText = await SseHelper.StreamAndAccumulateAsync(
                        Response, _provider.ChatStreamAsync(streamRequest, ct), _jsonOpts, ct);

                    if (fullText.Length > 0)
                        await _chatService.SaveAssistantResponseAsync(sessionId, fullText, ct);
                    break;

                case ChatResultType.PendingAction:
                    if (!string.IsNullOrEmpty(result.Text))
                        await SseHelper.WriteContentAsync(Response, result.Text, _jsonOpts, ct);

                    await SseHelper.WritePendingActionAsync(Response, result.PendingAction!, _jsonOpts, ct);
                    break;

                case ChatResultType.Error:
                    await SseHelper.WriteContentAsync(Response, result.Text, _jsonOpts, ct);
                    break;
            }

            await SseHelper.WriteDoneAsync(Response, ct);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
    }

    /// <summary>
    /// Confirm and execute a pending write action proposed by the AI.
    /// Streams the AI's summary of the result via SSE.
    /// </summary>
    [HttpPost("chat/confirm-action")]
    public async Task ConfirmAction([FromBody] ConfirmActionDto dto, CancellationToken ct)
    {
        if (!_provider.IsAvailable)
        {
            Response.StatusCode = 503;
            await Response.WriteAsync("{\"error\":\"AI provider not configured\"}", ct);
            return;
        }

        if (string.IsNullOrEmpty(dto.SessionId) || string.IsNullOrEmpty(dto.ToolName))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("{\"error\":\"Missing sessionId or toolName\"}", ct);
            return;
        }

        SseHelper.SetupResponse(Response);

        try
        {
            var locale = dto.Locale ?? _locale;
            var isChinese = locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

            // Execute the tool
            var toolResult = await _toolExecutor.ExecuteToolAsync(dto.ToolName, dto.Arguments ?? "{}", ct);

            // Build a follow-up AI request with the tool result in context
            var resultSummary = JsonSerializer.Serialize(toolResult, _jsonOpts);

            var followUpRequest = new AIChatRequest
            {
                SystemPrompt = await _chatService.BuildSystemPromptAsync(locale, ct),
                Messages =
                [
                    new AIChatMessage
                    {
                        Role = "user",
                        Content = isChinese
                            ? $"用户已确认执行操作: {dto.ToolName}，参数: {dto.Arguments ?? "{}"}"
                            : $"User confirmed action: {dto.ToolName} with arguments: {dto.Arguments ?? "{}"}"
                    },
                    new AIChatMessage
                    {
                        Role = "assistant",
                        Content = isChinese
                            ? $"工具执行结果: {resultSummary}"
                            : $"Tool result: {resultSummary}"
                    }
                ],
                Temperature = 0.3,
                MaxTokens = 1024
            };

            // Get AI summary via streaming
            var fullResponse = await SseHelper.StreamAndAccumulateAsync(
                Response, _provider.ChatStreamAsync(followUpRequest, ct), _jsonOpts, ct);

            if (fullResponse.Length > 0)
                await _chatService.SaveAssistantResponseAsync(dto.SessionId, fullResponse, ct);

            await SseHelper.WriteDoneAsync(Response, ct);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        catch (Exception ex)
        {
            var errorMsg = IsChinese ? $"执行失败: {ex.Message}" : $"Execution failed: {ex.Message}";
            await SseHelper.WriteErrorAsync(Response, errorMsg, _jsonOpts, ct);
            await SseHelper.WriteDoneAsync(Response, ct);
        }
    }

    /// <summary>Get recent AI analysis results.</summary>
    [HttpGet("analysis")]
    public async Task<IActionResult> GetAnalysis([FromQuery] int count = 20, [FromQuery] string? type = null, CancellationToken ct = default)
    {
        var results = await _analysisRepo.GetRecentAsync(count, type, ct);
        return Ok(new
        {
            code = 200,
            data = results.Select(r => new
            {
                id = r.Id,
                type = r.AnalysisType,
                content = r.Content,
                severity = r.Severity,
                relatedRoutes = r.RelatedRoutes,
                relatedClusters = r.RelatedClusters,
                createdAt = r.CreatedAt.ToString("O")
            })
        });
    }

    /// <summary>List chat sessions.</summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions([FromQuery] int count = 50, CancellationToken ct = default)
    {
        var sessions = await _conversationRepo.ListSessionsAsync(count, ct);
        return Ok(new
        {
            code = 200,
            data = sessions.Select(s => new
            {
                sessionId = s.SessionId,
                messageCount = s.MessageCount,
                lastActivity = s.LastActivity.ToString("O")
            })
        });
    }

    /// <summary>Get messages for a specific session.</summary>
    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetSessionMessages(string sessionId, [FromQuery] int count = 50, CancellationToken ct = default)
    {
        var messages = await _conversationRepo.GetSessionMessagesAsync(sessionId, count, ct);
        return Ok(new
        {
            code = 200,
            data = messages.Select(m => new
            {
                role = m.Role,
                content = m.Content,
                functionCalls = m.FunctionCalls,
                createdAt = m.CreatedAt.ToString("O")
            })
        });
    }

    /// <summary>Delete a chat session.</summary>
    [HttpDelete("sessions/{sessionId}")]
    public async Task<IActionResult> DeleteSession(string sessionId, CancellationToken ct = default)
    {
        await _conversationRepo.DeleteSessionAsync(sessionId, ct);
        return Ok(new { code = 200, message = "Session deleted" });
    }

    /// <summary>Manually trigger an AI analysis of current gateway state.</summary>
    [HttpPost("analyze")]
    public async Task<IActionResult> TriggerAnalysis(CancellationToken ct)
    {
        if (!_provider.IsAvailable)
            return StatusCode(503, new { error = "AI provider not configured" });

        try
        {
            var context = await _contextProvider.BuildContextAsync(ct);

            var request = new AIChatRequest
            {
                SystemPrompt = "You are a gateway operations analyst. Provide a concise status report with actionable insights.",
                Messages = [new AIChatMessage
                {
                    Role = "user",
                    Content = $"Analyze the current gateway state and provide a brief report:\n\n{context}\n\nFocus on: health status, anomalies, and recommended actions. Keep it under 200 words."
                }],
                Model = _options.AnalysisModel,
                Temperature = 0.2,
                MaxTokens = 1024
            };

            var response = await _provider.ChatAsync(request, ct);
            var entry = new AIAnalysisEntry
            {
                AnalysisType = "manual_analysis",
                Content = response?.Content ?? "Analysis completed but returned empty response.",
                Severity = 0,
                CreatedAt = DateTime.UtcNow
            };

            await _analysisRepo.SaveAnalysisAsync(entry, ct);

            return Ok(new { code = 200, data = new { entry.Id, entry.AnalysisType, entry.Content, entry.CreatedAt } });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Analysis failed: {ex.Message}" });
        }
    }

    /// <summary>Delete an analysis result.</summary>
    [HttpDelete("analysis/{id:long}")]
    public async Task<IActionResult> DeleteAnalysis(long id, CancellationToken ct = default)
    {
        await _analysisRepo.DeleteByIdAsync(id, ct);
        return Ok(new { code = 200, message = "Analysis deleted" });
    }
}
