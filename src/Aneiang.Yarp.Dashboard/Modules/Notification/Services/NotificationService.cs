using System.Collections.Concurrent;
using System.Text.Json;
using Aneiang.Yarp.Dashboard.Modules.AI.Services;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Services;

/// <summary>
/// Unified notification service: settings caching, rule matching, event dispatching.
/// Channel delivery is delegated to <see cref="ChannelSender"/>.
/// Cooldown management is delegated to <see cref="CooldownManager"/>.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repository;
    private readonly ILogger<NotificationService> _logger;
    private readonly ChannelSender _channelSender;
    private readonly CooldownManager _cooldownManager;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly NotificationEnhancer? _aiEnhancer;

    private volatile string _locale = "zh-CN";
    private NotificationSettingsEntity? _cachedSettings;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public NotificationService(
        INotificationRepository repository,
        ILogger<NotificationService> logger,
        IHttpClientFactory httpClientFactory,
        CooldownManager cooldownManager,
        NotificationEnhancer? aiEnhancer = null)
    {
        _repository = repository;
        _logger = logger;
        _channelSender = new ChannelSender(repository, httpClientFactory, logger);
        _cooldownManager = cooldownManager;
        _aiEnhancer = aiEnhancer;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>Preloads notification settings into memory cache.</summary>
    public async Task PreloadAsync(CancellationToken ct = default)
    {
        _cachedSettings = await _repository.LoadSettingsAsync(ct);
        _cacheExpiry = DateTime.Now.AddMinutes(5);
        _logger.LogDebug("NotificationService preloaded");
    }

    /// <summary>Gets notification settings with cache. Refreshes from repository when expired.</summary>
    private async Task<NotificationSettingsEntity> GetSettingsAsync(CancellationToken ct)
    {
        if (_cachedSettings != null && DateTime.Now < _cacheExpiry)
            return _cachedSettings;

        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cachedSettings != null && DateTime.Now < _cacheExpiry)
                return _cachedSettings;

            _cachedSettings = await _repository.LoadSettingsAsync(ct) ?? new NotificationSettingsEntity();

            // Sync the global-settings Enabled flag into SettingsEntity.Enabled so the UI toggle
            // (which writes into the JSON column) actually controls whether notifications fire.
            var globalSettings = await _repository.GetGlobalSettingsAsync(ct);
            if (globalSettings != null)
            {
                _cachedSettings.Enabled = globalSettings.Enabled;
            }

            _cacheExpiry = DateTime.Now.AddMinutes(5);
            return _cachedSettings;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>Invalidates the settings cache so the next access reloads from repository.</summary>
    public void InvalidateCache() => _cacheExpiry = DateTime.MinValue;

    /// <summary>
    /// Known config-change event types. When a rule specifies "ConfigChange" (legacy umbrella type),
    /// we expand it to match any of these concrete sub-types.
    /// </summary>
    private static readonly HashSet<string> s_configChangeSubTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddRoute", "UpdateRoute", "RemoveRoute",
        "AddCluster", "UpdateCluster", "RemoveCluster",
        "RollbackConfig"
    };

    /// <summary>Sends a notification event through all matching rules and records history.</summary>
    public async Task NotifyAsync(NotificationEvent evt, CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("[Notification] NotifyAsync called: {EventType}, severity={Severity}",
                evt.EventType, evt.Severity);
            var settings = await GetSettingsAsync(ct);

            if (!settings.Enabled)
            {
                _logger.LogDebug("Notification suppressed: notifications disabled");
                return;
            }

            // Load locale for i18n message generation
            var gs = await _repository.GetGlobalSettingsAsync(ct);
            if (!string.IsNullOrEmpty(gs.Locale)) _locale = gs.Locale;

            // ── AI Enhancement: enrich Warning/Error messages with context ──
            if (_aiEnhancer != null && _aiEnhancer.IsEnabled
                && (evt.Severity == NotificationSeverity.Warning || evt.Severity == NotificationSeverity.Error))
            {
                try
                {
                    var enhanced = await _aiEnhancer.TryEnhanceAsync(
                        evt.EventType, evt.Message, evt.Severity.ToString(),
                        evt.ClusterId, evt.RouteId, ct);
                    if (enhanced != evt.Message)
                    {
                        evt.Message = enhanced;
                        _logger.LogDebug("[Notification] AI-enhanced message for {EventType}", evt.EventType);
                    }
                }
                catch (Exception enhEx)
                {
                    _logger.LogDebug(enhEx, "[Notification] AI enhancement failed for {EventType}", evt.EventType);
                }
            }

            // ── Prepare history record (save after delivery so DeliverySuccess is accurate) ──
            var history = new NotificationHistory
            {
                Id = evt.Id,
                EventType = evt.EventType,
                Severity = evt.Severity,
                Title = evt.Title,
                Message = evt.Message,
                Timestamp = evt.Timestamp,
                ClusterId = evt.ClusterId,
                RouteId = evt.RouteId,
                ClientIp = evt.ClientIp,
                BlockReason = evt.Metadata.TryGetValue("blockReason", out var br) ? br : null,
                RequestUri = evt.Metadata.TryGetValue("requestUri", out var ru) ? ru : null,
                ErrorMessage = evt.Metadata.TryGetValue("errorMessage", out var em) ? em : null,
                AttemptCount = evt.Metadata.TryGetValue("attemptCount", out var ac) && int.TryParse(ac, out var a) ? a : null,
                LastStatusCode = evt.Metadata.TryGetValue("lastStatusCode", out var ls) && int.TryParse(ls, out var l) ? l : null,
                NotifiedChannels = [],
                DeliverySuccess = false
            };

            // ── Check rules for channel delivery ──
            var rules = await _repository.GetRulesAsync(ct);
            var channels = await _repository.GetChannelsAsync(ct);

            var matchingRules = rules
                .Where(r => r.Enabled && MatchesRule(r, evt))
                .ToList();

            var notifiedChannelNames = new List<string>();
            var anySuccess = false;

            if (matchingRules.Count > 0)
            {
                foreach (var rule in matchingRules)
                {
                    foreach (var channelId in rule.ChannelIds)
                    {
                        if (!channels.Any(c => c.Id == channelId && c.Enabled))
                            continue;

                        var cooldownKey = CooldownManager.GetCooldownKey(rule.Id, channelId, evt);
                        if (!_cooldownManager.TryAcquire(cooldownKey, TimeSpan.FromSeconds(rule.CooldownSeconds)))
                            continue;

                        var channel = channels.First(c => c.Id == channelId);
                        var success = await _channelSender.SendToChannelAsync(channel, evt, rule, ct);
                        if (success) anySuccess = true;
                        notifiedChannelNames.Add(channel.Name);
                    }
                }

                history.NotifiedChannels = notifiedChannelNames;
                history.DeliverySuccess = anySuccess;
            }

            // ── Always save history with accurate delivery result ──
            history.Message = SafeErrorMessages.Redact(history.Message);
            history.ErrorMessage = SafeErrorMessages.Redact(history.ErrorMessage);
            history.BlockReason = SafeErrorMessages.Redact(history.BlockReason);
            history.RequestUri = SafeErrorMessages.Redact(history.RequestUri);
            await _repository.RecordNotificationAsync(history, ct);
            _logger.LogInformation(
                "[Notification] History recorded: {EventType} severity={Severity} channels=[{Channels}] delivered={Delivered}",
                history.EventType, history.Severity,
                string.Join(", ", notifiedChannelNames), anySuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Notification] Unhandled error in NotifyAsync for event {EventType}: {ErrorMessage}",
                evt.EventType, ex.Message);
            // Do NOT rethrow — all callers use fire-and-forget pattern
        }
    }

    /// <summary>Checks whether a notification event matches a rule's conditions.</summary>
    private bool MatchesRule(NotificationRule rule, NotificationEvent evt)
    {
        // Check event types (empty list = all events)
        if (rule.EventTypes.Count > 0)
        {
            var directMatch = rule.EventTypes.Contains(evt.EventType);
            var legacyMatch = rule.EventTypes.Contains("ConfigChange") && s_configChangeSubTypes.Contains(evt.EventType);
            if (!directMatch && !legacyMatch)
            {
                _logger.LogDebug(
                    "[Notification] Rule '{RuleName}' skipped: event type '{EventType}' not in rule types [{RuleTypes}]",
                    rule.Name, evt.EventType, string.Join(", ", rule.EventTypes));
                return false;
            }
        }

        if (rule.TargetRouteIds?.Count > 0 && !string.IsNullOrEmpty(evt.RouteId))
        {
            if (!rule.TargetRouteIds.Contains(evt.RouteId))
            {
                _logger.LogDebug(
                    "[Notification] Rule '{RuleName}' skipped: route '{RouteId}' not in target routes",
                    rule.Name, evt.RouteId);
                return false;
            }
        }

        if (rule.TargetClusterIds?.Count > 0 && !string.IsNullOrEmpty(evt.ClusterId))
        {
            if (!rule.TargetClusterIds.Contains(evt.ClusterId))
            {
                _logger.LogDebug(
                    "[Notification] Rule '{RuleName}' skipped: cluster '{ClusterId}' not in target clusters",
                    rule.Name, evt.ClusterId);
                return false;
            }
        }

        _logger.LogDebug(
            "[Notification] Rule '{RuleName}' MATCHED for event '{EventType}'",
            rule.Name, evt.EventType);
        return true;
    }

    /// <summary>Sends a test notification to a specific channel.</summary>
    public async Task<bool> TestChannelAsync(string channelId, CancellationToken ct = default)
    {
        return await _channelSender.TestChannelAsync(channelId, ct);
    }

    /// <summary>
    /// Fire-and-forget helper: schedules the task and logs any unhandled exception.
    /// Prevents Task-level exceptions (including <see cref="OperationCanceledException"/>)
    /// from being silently swallowed.
    /// </summary>
    private void FireAndForget(Task task)
    {
        task.ContinueWith(t =>
        {
            if (t.IsFaulted)
                _logger.LogError(t.Exception, "Notification fire-and-forget failed");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Notifies a circuit breaker open event for the specified cluster.</summary>
    public void NotifyCircuitBreakerOpen(string clusterId, string? destinationId = null)
    {
        var destInfo = destinationId != null ? $" (destination: {destinationId})" : "";
        FireAndForget(NotifyAsync(new NotificationEvent
        {
            EventType = "CircuitBreakerOpen",
            Title = NotificationI18n.GetTitle("CircuitBreakerOpen", _locale, clusterId),
            Message = NotificationI18n.GetBody("CircuitBreakerOpen", _locale, clusterId)
                      + (destinationId != null ? $" (destination: {destinationId})" : ""),
            Severity = NotificationSeverity.Warning,
            ClusterId = clusterId,
            Metadata = destinationId != null ? new() { ["destinationId"] = destinationId } : new()
        }));
    }

    /// <summary>Notifies a retry exhausted event for the specified route and cluster.</summary>
    public void NotifyRetryExhausted(string clusterId, string routeId, int attempts, int statusCode)
    {
        FireAndForget(NotifyAsync(new NotificationEvent
        {
            EventType = "RetryExhausted",
            Title = NotificationI18n.GetTitle("RetryExhausted", _locale),
            Message = NotificationI18n.GetBody("RetryExhausted", _locale,
                routeId, attempts, clusterId, statusCode),
            Severity = NotificationSeverity.Error,
            ClusterId = clusterId,
            RouteId = routeId,
            Metadata = new()
            {
                ["attempts"] = attempts.ToString(),
                ["lastStatusCode"] = statusCode.ToString()
            }
        }));
    }

    /// <summary>Notifies a WAF block event for the specified client IP.</summary>
    public void NotifyWafBlock(string clientIp, string blockReason, string? uri = null)
    {
        FireAndForget(NotifyAsync(new NotificationEvent
        {
            EventType = "WafBlock",
            Title = NotificationI18n.GetTitle("WafBlock", _locale),
            Message = NotificationI18n.GetBody("WafBlock", _locale, clientIp, blockReason),
            Severity = NotificationSeverity.Warning,
            ClientIp = clientIp,
            Metadata = new()
            {
                ["blockReason"] = blockReason,
                ["requestUri"] = uri ?? "N/A"
            }
        }));
    }

    /// <summary>Notifies a proxy error event for the specified cluster and destination.</summary>
    public void NotifyProxyError(string clusterId, string? destinationId, string errorMessage)
    {
        var destInfo = destinationId != null ? $" (destination: {destinationId})" : "";
        FireAndForget(NotifyAsync(new NotificationEvent
        {
            EventType = "ProxyError",
            Title = NotificationI18n.GetTitle("ProxyError", _locale, clusterId),
            Message = NotificationI18n.GetBody("ProxyError", _locale,
                clusterId, destInfo, errorMessage),
            Severity = NotificationSeverity.Error,
            ClusterId = clusterId,
            Metadata = new()
            {
                ["errorMessage"] = errorMessage,
                ["destinationId"] = destinationId ?? ""
            }
        }));
    }

    public void NotifyRateLimitExceeded(string clientIp, string? routeId = null)
    {
        var routeInfo = routeId != null ? $" on route '{routeId}'" : "";
        FireAndForget(NotifyAsync(new NotificationEvent
        {
            EventType = "RateLimitExceeded",
            Title = NotificationI18n.GetTitle("RateLimitExceeded", _locale),
            Message = NotificationI18n.GetBody("RateLimitExceeded", _locale,
                clientIp, routeInfo),
            Severity = NotificationSeverity.Warning,
            ClientIp = clientIp,
            RouteId = routeId
        }));
    }

    /// <summary>Notify config change event (AddRoute, UpdateRoute, RemoveRoute, etc.).</summary>
    public void NotifyConfigChange(string eventType, string target, string? operatorName = null, object? details = null)
    {
        var op = operatorName ?? "system";
        var eventLabel = NotificationI18n.GetTitle(eventType, _locale, eventType);

        var detailsStr = details switch
        {
            null => null,
            JsonElement je => JsonSerializer.Serialize(je, _jsonOptions),
            string s => s,
            _ => JsonSerializer.Serialize(details, _jsonOptions)
        };

        var sev = eventType switch
        {
            "RemoveRoute" or "RemoveCluster" => NotificationSeverity.Warning,
            "RollbackConfig" => NotificationSeverity.Warning,
            _ => NotificationSeverity.Info
        };

        var body = NotificationI18n.GetBody("configChange", _locale, op, target, eventLabel)
                   + (detailsStr != null ? $"\nDetails: {detailsStr}" : "");

        FireAndForget(NotifyAsync(new NotificationEvent
        {
            EventType = eventType,
            Title = NotificationI18n.GetTitle("configChange", _locale, eventLabel),
            Message = body,
            Severity = sev,
            Operator = operatorName,
            Metadata = new()
            {
                ["target"] = target,
                ["details"] = detailsStr ?? ""
            }
        }));
    }

    /// <summary>Sends a custom notification with the specified event type, title, and message.</summary>
    public void NotifyCustom(string eventType, string title, string message)
    {
        FireAndForget(NotifyAsync(new NotificationEvent
        {
            EventType = eventType,
            Title = title,
            Message = message,
            Severity = NotificationSeverity.Info
        }));
    }
}
