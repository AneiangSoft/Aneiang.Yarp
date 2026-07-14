using System.Collections.Concurrent;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Services;

/// <summary>
/// Enhances notification messages with AI-generated context and suggested actions.
/// Only enhances Warning/Critical severity notifications to minimize API costs.
/// Uses a cooldown to prevent excessive AI calls during notification storms.
/// </summary>
public class NotificationEnhancer
{
    private readonly IAIProvider _provider;
    private readonly GatewayContextProvider _contextProvider;
    private readonly AIOptions _options;
    private readonly ILogger<NotificationEnhancer> _logger;

    // Cooldown tracking: key = event type + cluster/route, value = last enhance time
    private readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();

    public NotificationEnhancer(
        IAIProvider provider,
        GatewayContextProvider contextProvider,
        IOptions<AIOptions> options,
        ILogger<NotificationEnhancer> logger)
    {
        _provider = provider;
        _contextProvider = contextProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Whether notification enhancement is enabled and the provider is available.
    /// </summary>
    public bool IsEnabled => _options.EnhanceNotifications && _provider.IsAvailable;

    /// <summary>
    /// Try to enhance a notification message with AI-generated context.
    /// Returns the enhanced message, or the original if enhancement is skipped.
    /// </summary>
    public async Task<string> TryEnhanceAsync(
        string eventType,
        string originalMessage,
        string severity,
        string? clusterId = null,
        string? routeId = null,
        CancellationToken ct = default)
    {
        if (!IsEnabled)
            return originalMessage;

        // Only enhance Warning and Error severities
        if (severity != "Warning" && severity != "Error" && severity != "Critical")
            return originalMessage;

        // Check cooldown
        var cooldownKey = $"{eventType}:{clusterId}:{routeId}";
        if (_cooldowns.TryGetValue(cooldownKey, out var lastEnhance))
        {
            if ((DateTime.Now - lastEnhance).TotalSeconds < _options.NotificationEnhanceCooldownSeconds)
                return originalMessage;
        }

        try
        {
            _cooldowns[cooldownKey] = DateTime.Now;

            var context = await _contextProvider.BuildContextAsync(ct);

            var prompt = $"""
                A gateway notification was triggered. Provide brief context and suggested actions.

                Notification: {eventType}
                Severity: {severity}
                Cluster: {clusterId ?? "N/A"}
                Route: {routeId ?? "N/A"}
                Original message: {originalMessage}

                {context}

                Respond in this exact format:
                {originalMessage}

                **AI Analysis:** [1-2 sentences about likely cause]
                **Suggested Actions:**
                1. [action]
                2. [action]

                Keep total response under 100 words.
                """;

            var request = new AIChatRequest
            {
                SystemPrompt = "You are a gateway operations assistant. Enhance alert notifications with brief context and actionable suggestions.",
                Messages = [new() { Role = "user", Content = prompt }],
                Model = _options.AnalysisModel,
                Temperature = 0.2,
                MaxTokens = 256
            };

            var response = await _provider.ChatAsync(request, ct);
            if (response?.Content is { Length: > 0 })
            {
                _logger.LogDebug("[AI-Enhance] Enhanced notification '{EventType}': {Length} chars",
                    eventType, response.Content.Length);
                return response.Content;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AI-Enhance] Failed to enhance notification '{EventType}': {Message}",
                eventType, ex.Message);
        }

        return originalMessage;
    }
}
