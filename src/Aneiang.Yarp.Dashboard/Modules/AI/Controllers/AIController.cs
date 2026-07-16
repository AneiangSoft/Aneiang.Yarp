using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Modules.AI;
using Aneiang.Yarp.Dashboard.Modules.AI.Application;
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
/// Non-SSE endpoints delegate to <see cref="IAIAppService"/>.
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
    private readonly IChatService _chatService;
    private readonly IAIAppService _appService;
    private readonly GatewayToolExecutor _toolExecutor;
    private readonly string _locale;

    public AIController(
        IAIProvider provider,
        IChatService chatService,
        IAIAppService appService,
        GatewayToolExecutor toolExecutor,
        IOptions<DashboardOptions> dashboardOptions)
    {
        _provider = provider;
        _chatService = chatService;
        _appService = appService;
        _toolExecutor = toolExecutor;
        _locale = dashboardOptions.Value.Locale ?? "zh-CN";
    }

    private bool IsChinese => _locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    [HttpGet("status")]
    public IActionResult GetStatus() => Ok(ApiResponse.Ok(_appService.GetStatus()));

    [HttpGet("settings")]
    public IActionResult GetSettings() => Ok(ApiResponse.Ok(_appService.GetSettings()));

    [HttpPut("settings")]
    public IActionResult UpdateSettings([FromBody] AISettingsDto dto)
    {
        _appService.UpdateSettings(dto);
        return Ok(ApiResponse.Ok("AI settings updated"));
    }

    [HttpGet("tools")]
    public IActionResult GetTools() => Ok(ApiResponse.Ok(_appService.GetTools()));

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
                MaxTokens = 10, Temperature = 0
            };
            var response = await _provider.ChatAsync(request, ct);
            return Ok(new { code = 200, success = true, message = $"Connected to {_provider.ProviderName}", tokensUsed = response.PromptTokens + response.CompletionTokens });
        }
        catch (Exception ex)
        {
            return Ok(new { code = 200, success = false, error = ex.Message });
        }
    }

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

        var sessionId = await _chatService.ProcessMessageAsync(request.SessionId, userMessage.Content, ct);
        var locale = request.Locale ?? _locale;
        var aiRequest = await _chatService.BuildChatRequestAsync(sessionId, locale, ct);

        SseHelper.SetupResponse(Response, sessionId);

        try
        {
            var result = await _chatService.ProcessWithToolsAsync(sessionId, aiRequest, locale, ct);

            switch (result.Type)
            {
                case ChatResultType.Text:
                    var streamRequest = new AIChatRequest
                    {
                        SystemPrompt = aiRequest.SystemPrompt,
                        Messages = result.AccumulatedMessages ?? aiRequest.Messages,
                        Tools = null, Model = aiRequest.Model, Temperature = 0.3, MaxTokens = aiRequest.MaxTokens
                    };
                    var fullText = await SseHelper.StreamAndAccumulateAsync(Response, _provider.ChatStreamAsync(streamRequest, ct), _jsonOpts, ct);
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
        catch (OperationCanceledException) { }
    }

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
            var toolResult = await _toolExecutor.ExecuteToolAsync(dto.ToolName, dto.Arguments ?? "{}", ct);
            var resultSummary = JsonSerializer.Serialize(toolResult, _jsonOpts);

            var followUpRequest = new AIChatRequest
            {
                SystemPrompt = await _chatService.BuildSystemPromptAsync(locale, ct),
                Messages =
                [
                    new AIChatMessage { Role = "user", Content = isChinese ? $"用户已确认执行操作: {dto.ToolName}，参数: {dto.Arguments ?? "{}"}" : $"User confirmed action: {dto.ToolName} with arguments: {dto.Arguments ?? "{}"}" },
                    new AIChatMessage { Role = "assistant", Content = isChinese ? $"工具执行结果: {resultSummary}" : $"Tool result: {resultSummary}" }
                ],
                Temperature = 0.3, MaxTokens = 1024
            };

            var fullResponse = await SseHelper.StreamAndAccumulateAsync(Response, _provider.ChatStreamAsync(followUpRequest, ct), _jsonOpts, ct);
            if (fullResponse.Length > 0)
                await _chatService.SaveAssistantResponseAsync(dto.SessionId, fullResponse, ct);
            await SseHelper.WriteDoneAsync(Response, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            var errorMsg = IsChinese ? $"执行失败: {ex.Message}" : $"Execution failed: {ex.Message}";
            await SseHelper.WriteErrorAsync(Response, errorMsg, _jsonOpts, ct);
            await SseHelper.WriteDoneAsync(Response, ct);
        }
    }

    [HttpGet("analysis")]
    public async Task<IActionResult> GetAnalysis([FromQuery] int count = 20, [FromQuery] string? type = null, CancellationToken ct = default)
        => Ok(ApiResponse.Ok(await _appService.GetAnalysisAsync(count, type, ct)));

    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions([FromQuery] int count = 50, CancellationToken ct = default)
        => Ok(ApiResponse.Ok(await _appService.ListSessionsAsync(count, ct)));

    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetSessionMessages(string sessionId, [FromQuery] int count = 50, CancellationToken ct = default)
        => Ok(ApiResponse.Ok(await _appService.GetSessionMessagesAsync(sessionId, count, ct)));

    [HttpDelete("sessions/{sessionId}")]
    public async Task<IActionResult> DeleteSession(string sessionId, CancellationToken ct = default)
    {
        await _appService.DeleteSessionAsync(sessionId, ct);
        return Ok(ApiResponse.Ok("Session deleted"));
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> TriggerAnalysis(CancellationToken ct)
    {
        if (!_provider.IsAvailable)
            return StatusCode(503, new { code = 503, success = false, error = "AI provider not configured" });

        try { return Ok(ApiResponse.Ok(await _appService.TriggerAnalysisAsync(ct))); }
        catch (Exception ex) { return StatusCode(500, new { code = 500, success = false, error = $"Analysis failed: {ex.Message}" }); }
    }

    [HttpDelete("analysis/{id:long}")]
    public async Task<IActionResult> DeleteAnalysis(long id, CancellationToken ct = default)
    {
        await _appService.DeleteAnalysisAsync(id, ct);
        return Ok(ApiResponse.Ok("Analysis deleted"));
    }
}
