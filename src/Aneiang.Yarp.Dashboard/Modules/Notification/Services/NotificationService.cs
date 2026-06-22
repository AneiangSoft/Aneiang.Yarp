using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Services;

/// <summary>
/// Unified notification service that handles sending notifications to channels
/// based on configured rules. Replaces the old Alert system.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repository;
    private readonly ILogger<NotificationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JsonSerializerOptions _jsonOptions;

    private static readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _channelLocks = new();

    private string _locale = "zh-CN";
    private NotificationSettingsEntity? _cachedSettings;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public NotificationService(
        INotificationRepository repository,
        ILogger<NotificationService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _repository = repository;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task PreloadAsync(CancellationToken ct = default)
    {
        _cachedSettings = await _repository.LoadSettingsAsync(ct);
        _cacheExpiry = DateTime.UtcNow.AddMinutes(5);
        _logger.LogDebug("NotificationService preloaded");
    }

    private async Task<NotificationSettingsEntity> GetSettingsAsync(CancellationToken ct)
    {
        if (_cachedSettings != null && DateTime.UtcNow < _cacheExpiry)
            return _cachedSettings;

        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cachedSettings != null && DateTime.UtcNow < _cacheExpiry)
                return _cachedSettings;

            _cachedSettings = await _repository.LoadSettingsAsync(ct) ?? new NotificationSettingsEntity();

            // Sync the global-settings Enabled flag into SettingsEntity.Enabled so the UI toggle
            // (which writes into the JSON column) actually controls whether notifications fire.
            var globalSettings = await _repository.GetGlobalSettingsAsync(ct);
            if (globalSettings != null)
            {
                _cachedSettings.Enabled = globalSettings.Enabled;
            }

            _cacheExpiry = DateTime.UtcNow.AddMinutes(5);
            return _cachedSettings;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public void InvalidateCache() => _cacheExpiry = DateTime.MinValue;

    // ─── Event Notification ───────────────────────────────────────────────────

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

                        var cooldownKey = $"{rule.Id}:{channelId}:{GetCooldownTarget(evt)}";
                        if (!TryAcquireCooldown(cooldownKey, TimeSpan.FromSeconds(rule.CooldownSeconds)))
                            continue;

                        var channel = channels.First(c => c.Id == channelId);
                        var success = await SendToChannelAsync(channel, evt, rule, ct);
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

    private bool MatchesRule(NotificationRule rule, NotificationEvent evt)
    {
        // All events match regardless of severity (severity filtering removed per user request)

        // Check event types (empty list = all events)
        if (rule.EventTypes.Count > 0)
        {
            // Direct match: event type is explicitly listed in the rule
            var directMatch = rule.EventTypes.Contains(evt.EventType);
            // Legacy compatibility: "ConfigChange" is an umbrella type that matches any concrete config-change sub-type
            var legacyMatch = rule.EventTypes.Contains("ConfigChange") && s_configChangeSubTypes.Contains(evt.EventType);
            if (!directMatch && !legacyMatch)
            {
                _logger.LogDebug(
                    "[Notification] Rule '{RuleName}' skipped: event type '{EventType}' not in rule types [{RuleTypes}]",
                    rule.Name, evt.EventType, string.Join(", ", rule.EventTypes));
                return false;
            }
        }

        // Check target routes
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

        // Check target clusters
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

    private static string GetCooldownTarget(NotificationEvent evt)
    {
        return evt.ClusterId ?? evt.RouteId ?? evt.ClientIp ?? "global";
    }

    private static bool TryAcquireCooldown(string key, TimeSpan cooldown)
    {
        var now = DateTime.UtcNow;
        if (_cooldowns.TryGetValue(key, out var lastAlert))
        {
            if (now - lastAlert < cooldown)
                return false;
        }
        _cooldowns[key] = now;
        return true;
    }

    // ─── Channel Delivery ───────────────────────────────────────────────────

    private async Task<bool> SendToChannelAsync(
        NotificationChannel channel,
        NotificationEvent evt,
        NotificationRule rule,
        CancellationToken ct)
    {
        var lockKey = channel.Id;
        var semaphore = _channelLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(ct);
        try
        {
            var globalSettings = await _repository.GetGlobalSettingsAsync(ct);
            var timeout = globalSettings.DefaultTimeoutSeconds > 0 ? globalSettings.DefaultTimeoutSeconds : 10;
            var retryCount = globalSettings.DefaultRetryCount >= 0 ? globalSettings.DefaultRetryCount : 1;

            for (var attempt = 0; attempt <= retryCount; attempt++)
            {
                try
                {
                    var payload = BuildPayload(channel.Type, evt);
                    var ok = await SendHttpRequestAsync(channel, payload, timeout, ct);
                    if (ok)
                    {
            _logger.LogDebug(
                "[Notification] Successfully sent event {EventType} to channel {ChannelName} (attempt {Attempt})",
                evt.EventType, channel.Name, attempt + 1);
                        return true;
                    }

                    _logger.LogWarning(
                        "[Notification] Channel {ChannelName} returned failure on attempt {Attempt} (will retry={WillRetry})",
                        channel.Name, attempt + 1, attempt < retryCount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[Notification] Exception sending to channel {ChannelName} on attempt {Attempt}: {Error} (will retry={WillRetry})",
                        channel.Name, attempt + 1, ex.Message, attempt < retryCount);
                }
            }

            _logger.LogError(
                "[Notification] All {TotalAttempts} attempts failed for channel {ChannelName}; event {EventType} was not delivered",
                retryCount + 1, channel.Name, evt.EventType);
            return false;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private object BuildPayload(NotificationChannelType type, NotificationEvent evt)
    {
        return type switch
        {
            NotificationChannelType.DingTalk => new DingTalkPayload
            {
                MsgType = "markdown",
                Markdown = new DingTalkMarkdown
                {
                    Title = evt.Title,
                    Text = BuildDingTalkText(evt)
                }
            },
            _ => new GenericWebhookPayload
            {
                EventType = evt.EventType,
                Title = evt.Title,
                Message = evt.Message,
                Timestamp = evt.Timestamp,
                ClusterId = evt.ClusterId,
                RouteId = evt.RouteId,
                ClientIp = evt.ClientIp,
                Metadata = evt.Metadata,
                GatewayName = Environment.MachineName
            }
        };
    }

    private string BuildDingTalkText(NotificationEvent evt)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### {evt.Title}");
        sb.AppendLine();
        sb.AppendLine($"> {evt.Message}");
        sb.AppendLine();
        sb.AppendLine($"- **Time**: {evt.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");

        if (!string.IsNullOrEmpty(evt.ClusterId))
            sb.AppendLine($"- **Cluster**: `{evt.ClusterId}`");
        if (!string.IsNullOrEmpty(evt.RouteId))
            sb.AppendLine($"- **Route**: `{evt.RouteId}`");
        if (!string.IsNullOrEmpty(evt.ClientIp))
            sb.AppendLine($"- **Client IP**: `{evt.ClientIp}`");

        return sb.ToString();
    }

    private async Task<bool> SendHttpRequestAsync(
        NotificationChannel channel,
        object payload,
        int timeoutSeconds,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("notification");
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        string url = channel.Url;

        // Add signature if secret is configured
        if (!string.IsNullOrEmpty(channel.Secret))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var stringToSign = timestamp + "\n" + channel.Secret;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(channel.Secret));
            var sign = Uri.EscapeDataString(Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign))));

            if (channel.Type == NotificationChannelType.DingTalk)
            {
                // DingTalk requires signature as URL query parameters
                var separator = url.Contains('?') ? "&" : "?";
                url = $"{url}{separator}timestamp={timestamp}&sign={sign}";
                _logger.LogDebug(
                    "[DingTalk] timestamp={Timestamp} sign={Sign} secretLen={SecretLen}",
                    timestamp, sign.Length > 8 ? sign[..8] + "..." : sign, channel.Secret.Length);
            }
            else
            {
                content.Headers.Add("X-Webhook-Signature", sign);
            }
        }

        _logger.LogDebug(
            "[Notification] Sending to channel {ChannelName} ({ChannelType}): POST {Url}",
            channel.Name, channel.Type, url);

        var response = await client.PostAsync(url, content, ct);
        var bodyText = await response.Content.ReadAsStringAsync(ct);

        _logger.LogDebug(
            "[Notification] Response from {ChannelName}: HTTP {StatusCode} body={Body}",
            channel.Name, (int)response.StatusCode, bodyText);

        // DingTalk returns 200 OK with an errcode field — check it explicitly
        if (channel.Type == NotificationChannelType.DingTalk && response.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(bodyText);
            if (doc.RootElement.TryGetProperty("errcode", out var errcode) && errcode.GetInt64() != 0)
            {
                var errmsg = doc.RootElement.TryGetProperty("errmsg", out var em) ? em.GetString() : "";
                _logger.LogWarning(
                    "[DingTalk] API error for channel {Channel}: errcode={Errcode} errmsg={Errmsg}",
                    channel.Name, errcode.GetInt64(), errmsg);
                return false;
            }
        }

        return response.IsSuccessStatusCode;
    }

    // ─── Test Notification ───────────────────────────────────────────────────

    public async Task<bool> TestChannelAsync(string channelId, CancellationToken ct = default)
    {
        var channel = await _repository.GetChannelAsync(channelId, ct);
        if (channel == null || !channel.Enabled)
            return false;

        var testEvent = new NotificationEvent
        {
            EventType = "TestNotification",
            Title = "Test Notification",
            Message = "This is a test notification from Aneiang.Yarp Gateway.",
            Severity = NotificationSeverity.Info,
            Timestamp = DateTime.UtcNow
        };

        var testRule = new NotificationRule { Id = "test" };
        return await SendToChannelAsync(channel, testEvent, testRule, ct);
    }

    // ─── Event Convenience Methods ─────────────────────────────────────────

    public void NotifyCircuitBreakerOpen(string clusterId, string? destinationId = null)
    {
        var destInfo = destinationId != null ? $" (destination: {destinationId})" : "";
        _ = NotifyAsync(new NotificationEvent
        {
            EventType = "CircuitBreakerOpen",
            Title = NotificationI18n.GetTitle("CircuitBreakerOpen", _locale, clusterId),
            Message = NotificationI18n.GetBody("CircuitBreakerOpen", _locale, clusterId)
                      + (destinationId != null ? $" (destination: {destinationId})" : ""),
            Severity = NotificationSeverity.Warning,
            ClusterId = clusterId,
            Metadata = destinationId != null ? new() { ["destinationId"] = destinationId } : new()
        });
    }

    public void NotifyRetryExhausted(string clusterId, string routeId, int attempts, int statusCode)
    {
        _ = NotifyAsync(new NotificationEvent
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
        });
    }

    public void NotifyWafBlock(string clientIp, string blockReason, string? uri = null)
    {
        _ = NotifyAsync(new NotificationEvent
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
        });
    }

    public void NotifyProxyError(string clusterId, string? destinationId, string errorMessage)
    {
        var destInfo = destinationId != null ? $" (destination: {destinationId})" : "";
        _ = NotifyAsync(new NotificationEvent
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
        });
    }

    public void NotifyRateLimitExceeded(string clientIp, string? routeId = null)
    {
        var routeInfo = routeId != null ? $" on route '{routeId}'" : "";
        _ = NotifyAsync(new NotificationEvent
        {
            EventType = "RateLimitExceeded",
            Title = NotificationI18n.GetTitle("RateLimitExceeded", _locale),
            Message = NotificationI18n.GetBody("RateLimitExceeded", _locale,
                clientIp, routeInfo),
            Severity = NotificationSeverity.Warning,
            ClientIp = clientIp,
            RouteId = routeId
        });
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

        _ = NotifyAsync(new NotificationEvent
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
        });
    }

    public void NotifyCustom(string eventType, string title, string message)
    {
        _ = NotifyAsync(new NotificationEvent
        {
            EventType = eventType,
            Title = title,
            Message = message,
            Severity = NotificationSeverity.Info
        });
    }
}

/// <summary>
/// Interface for the notification service.
/// </summary>
public interface INotificationService
{
    /// <summary>Preload settings into memory cache.</summary>
    Task PreloadAsync(CancellationToken ct = default);

    /// <summary>Invalidate the settings cache.</summary>
    void InvalidateCache();

    /// <summary>Send a notification event through all matching rules.</summary>
    Task NotifyAsync(NotificationEvent evt, CancellationToken ct = default);

    /// <summary>Send a test notification to a specific channel.</summary>
    Task<bool> TestChannelAsync(string channelId, CancellationToken ct = default);

    /// <summary>Notify circuit breaker open event.</summary>
    void NotifyCircuitBreakerOpen(string clusterId, string? destinationId = null);

    /// <summary>Notify retry exhausted event.</summary>
    void NotifyRetryExhausted(string clusterId, string routeId, int attempts, int statusCode);

    /// <summary>Notify WAF block event.</summary>
    void NotifyWafBlock(string clientIp, string blockReason, string? uri = null);

    /// <summary>Notify proxy error event.</summary>
    void NotifyProxyError(string clusterId, string? destinationId, string errorMessage);

    /// <summary>Notify rate limit exceeded event.</summary>
    void NotifyRateLimitExceeded(string clientIp, string? routeId = null);

    /// <summary>Notify config change event (AddRoute, UpdateRoute, RemoveRoute, etc.).</summary>
    void NotifyConfigChange(string eventType, string target, string? operatorName = null, object? details = null);

    /// <summary>Send a custom notification.</summary>
    void NotifyCustom(string eventType, string title, string message);
}

// ─── Webhook Payload Models ────────────────────────────────────────────────────

internal class DingTalkPayload
{
    [JsonPropertyName("msgtype")]
    public string MsgType { get; set; } = "text";
    [JsonPropertyName("markdown")]
    public DingTalkMarkdown? Markdown { get; set; }
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

internal class DingTalkMarkdown
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

internal class GenericWebhookPayload
{
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? ClusterId { get; set; }
    public string? RouteId { get; set; }
    public string? ClientIp { get; set; }
    public Dictionary<string, string?> Metadata { get; set; } = new();
    public string GatewayName { get; set; } = string.Empty;
}

/// <summary>
/// No-op notification service used when <see cref="INotificationService"/> is not available.
/// All methods are no-ops; callers should use the null-conditional pattern (e.g.,
/// <c>notificationService ?? NullNotificationService.Instance</c>).
/// </summary>
public sealed class NullNotificationService : INotificationService
{
    public static readonly NullNotificationService Instance = new();

    private NullNotificationService() { }

    public Task PreloadAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void InvalidateCache() { }
    public Task NotifyAsync(NotificationEvent evt, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> TestChannelAsync(string channelId, CancellationToken ct = default) => Task.FromResult(false);
    public void NotifyCircuitBreakerOpen(string clusterId, string? destinationId = null) { }
    public void NotifyRetryExhausted(string clusterId, string routeId, int attempts, int statusCode) { }
    public void NotifyWafBlock(string clientIp, string blockReason, string? uri = null) { }
    public void NotifyProxyError(string clusterId, string? destinationId, string errorMessage) { }
    public void NotifyRateLimitExceeded(string clientIp, string? routeId = null) { }
    public void NotifyConfigChange(string eventType, string target, string? operatorName = null, object? details = null) { }
    public void NotifyCustom(string eventType, string title, string message) { }
}
