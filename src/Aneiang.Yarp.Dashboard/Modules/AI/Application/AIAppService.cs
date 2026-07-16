using Aneiang.Yarp.Dashboard.Infrastructure.Exceptions;
using Aneiang.Yarp.Dashboard.Modules.AI;
using Aneiang.Yarp.Dashboard.Modules.AI.Services;
using Aneiang.Yarp.Dashboard.Modules.AI.Tools;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Application;

/// <summary>
/// Application service for AI operations: analysis triggering, status retrieval.
/// </summary>
public interface IAIAppService
{
    object GetStatus();
    object GetSettings();
    void UpdateSettings(AISettingsDto dto);
    object GetTools();
    Task<object> TriggerAnalysisAsync(CancellationToken ct);
    Task<object> GetAnalysisAsync(int count, string? type, CancellationToken ct);
    Task<object> ListSessionsAsync(int count, CancellationToken ct);
    Task<object> GetSessionMessagesAsync(string sessionId, int count, CancellationToken ct);
    Task DeleteSessionAsync(string sessionId, CancellationToken ct);
    Task DeleteAnalysisAsync(long id, CancellationToken ct);
}

/// <inheritdoc/>
public class AIAppService : IAIAppService
{
    private readonly IAIProvider _provider;
    private readonly IAISettingsService _settingsService;
    private readonly IChatService _chatService;
    private readonly IAIConversationRepository _conversationRepo;
    private readonly IAIAnalysisRepository _analysisRepo;
    private readonly GatewayContextProvider _contextProvider;
    private readonly GatewayToolRegistry _toolRegistry;
    private readonly AIOptions _options;

    public AIAppService(
        IAIProvider provider,
        IAISettingsService settingsService,
        IChatService chatService,
        IAIConversationRepository conversationRepo,
        IAIAnalysisRepository analysisRepo,
        GatewayContextProvider contextProvider,
        GatewayToolRegistry toolRegistry,
        IOptions<AIOptions> options)
    {
        _provider = provider;
        _settingsService = settingsService;
        _chatService = chatService;
        _conversationRepo = conversationRepo;
        _analysisRepo = analysisRepo;
        _contextProvider = contextProvider;
        _toolRegistry = toolRegistry;
        _options = options.Value;
    }

    public object GetStatus()
    {
        var settings = _settingsService.GetSettings();
        return new
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
        };
    }

    public object GetSettings() => _settingsService.GetSettings();

    public void UpdateSettings(AISettingsDto dto) => _settingsService.UpdateSettings(dto);

    public object GetTools()
    {
        var tools = _toolRegistry.GetToolDefinitions();
        return tools.Select(t => new { name = t.Name, description = t.Description, isReadOnly = t.IsReadOnly });
    }

    public async Task<object> TriggerAnalysisAsync(CancellationToken ct)
    {
        if (!_provider.IsAvailable)
            throw new ServerException("AI provider not configured");

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
            CreatedAt = DateTime.Now
        };

        await _analysisRepo.SaveAnalysisAsync(entry, ct);
        return new { entry.Id, entry.AnalysisType, entry.Content, entry.CreatedAt };
    }

    public async Task<object> GetAnalysisAsync(int count, string? type, CancellationToken ct)
    {
        var results = await _analysisRepo.GetRecentAsync(count, type, ct);
        return results.Select(r => new
        {
            id = r.Id,
            type = r.AnalysisType,
            content = r.Content,
            severity = r.Severity,
            relatedRoutes = r.RelatedRoutes,
            relatedClusters = r.RelatedClusters,
            createdAt = r.CreatedAt.ToString("O")
        });
    }

    public async Task<object> ListSessionsAsync(int count, CancellationToken ct)
    {
        var sessions = await _conversationRepo.ListSessionsAsync(count, ct);
        return sessions.Select(s => new
        {
            sessionId = s.SessionId,
            messageCount = s.MessageCount,
            lastActivity = s.LastActivity.ToString("O")
        });
    }

    public async Task<object> GetSessionMessagesAsync(string sessionId, int count, CancellationToken ct)
    {
        var messages = await _conversationRepo.GetSessionMessagesAsync(sessionId, count, ct);
        return messages.Select(m => new
        {
            role = m.Role,
            content = m.Content,
            functionCalls = m.FunctionCalls,
            createdAt = m.CreatedAt.ToString("O")
        });
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken ct)
        => _conversationRepo.DeleteSessionAsync(sessionId, ct);

    public Task DeleteAnalysisAsync(long id, CancellationToken ct)
        => _analysisRepo.DeleteByIdAsync(id, ct);
}
