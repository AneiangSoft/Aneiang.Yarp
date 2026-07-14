using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Services;

/// <summary>
/// Handles HTTP delivery of notifications to configured channels (DingTalk, Generic Webhook).
/// Extracted from <see cref="NotificationService"/> for single responsibility.
/// </summary>
internal sealed class ChannelSender
{
    private readonly INotificationRepository _repository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _channelLocks = new();

    /// <summary>
    /// Remove the lock for a channel that has been deleted.
    /// Prevents SemaphoreSlim objects from accumulating indefinitely.
    /// </summary>
    public static void RemoveChannelLock(string channelId)
    {
        if (_channelLocks.TryRemove(channelId, out var semaphore))
            semaphore.Dispose();
    }

    public ChannelSender(
        INotificationRepository repository,
        IHttpClientFactory httpClientFactory,
        ILogger logger)
    {
        _repository = repository;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>Send a notification event to a specific channel with retry.</summary>
    public async Task<bool> SendToChannelAsync(
        NotificationChannel channel,
        NotificationEvent evt,
        NotificationRule rule,
        CancellationToken ct = default)
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
            Timestamp = DateTime.Now
        };

        var testRule = new NotificationRule { Id = "test" };
        return await SendToChannelAsync(channel, testEvent, testRule, ct);
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

    private static string BuildDingTalkText(NotificationEvent evt)
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
}
