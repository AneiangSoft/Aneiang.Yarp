using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Notifications;

/// <summary>Supported notification platforms (2026 payload contracts).</summary>
public static class WebhookPlatforms
{
    public const string DingTalk = "dingtalk";
    public const string WeChat = "wechat";
    public const string Feishu = "feishu";
    public const string Slack = "slack";
    public const string Discord = "discord";
    public const string Telegram = "telegram";
    public const string Teams = "teams";
    public const string Generic = "generic";

    /// <summary>All known platforms, in UI display order.</summary>
    public static readonly string[] All =
    [
        DingTalk, WeChat, Feishu, Slack, Discord, Telegram, Teams, Generic
    ];

    public static bool IsKnown(string platform)
        => All.Contains(platform, StringComparer.OrdinalIgnoreCase);
}

/// <summary>A notification message to be delivered to webhook endpoints.</summary>
public sealed class WebhookNotificationMessage
{
    public string EventType { get; init; } = string.Empty;
    public string? Target { get; init; }
    public string? Operator { get; init; }

    /// <summary>Info | Warning | Error. Drives per-platform accent colors.</summary>
    public string Severity { get; init; } = "Info";

    /// <summary>Optional free-form detail body (used by alert events).</summary>
    public string? Detail { get; init; }
}

/// <summary>Outcome of a single webhook endpoint delivery attempt.</summary>
public sealed class WebhookDeliveryDetail
{
    public string Platform { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public int DurationMs { get; set; }
}

/// <summary>Aggregated delivery report across one or more endpoints.</summary>
public sealed class WebhookDeliveryReport
{
    public bool Success { get; set; }
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public List<WebhookDeliveryDetail> Details { get; set; } = new();
}

/// <summary>
/// Sends webhook notifications to 8 platforms using their 2026 payload contracts:
/// DingTalk (markdown + HMAC-SHA256 URL sign), WeCom (markdown), Feishu (interactive
/// card + HMAC-SHA256 body sign), Slack (Block Kit), Discord (embeds), Telegram
/// (Bot API sendMessage), Microsoft Teams (Power Automate Workflows Adaptive Card),
/// plus a generic JSON contract. Failed deliveries are retried with exponential
/// backoff and every attempt is recorded as delivery history.
/// </summary>
public sealed class WebhookNotificationService
{
    private static readonly TimeSpan MinSendTimeout = TimeSpan.FromSeconds(3);

    private readonly IHttpClientFactory _httpFactory;
    private readonly IServiceProvider _services;
    private readonly ILogger<WebhookNotificationService> _logger;

    public WebhookNotificationService(
        IHttpClientFactory httpFactory,
        IServiceProvider services,
        ILogger<WebhookNotificationService> logger)
    {
        _httpFactory = httpFactory;
        _services = services;
        _logger = logger;
    }

    /// <summary>Load persisted webhook settings.</summary>
    public async Task<WebhookSettingsData> GetSettingsAsync(CancellationToken ct = default)
    {
        using var scope = _services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWebhookSettingsRepository>();
        return await repo.LoadAsync(ct);
    }

    /// <summary>Persist webhook settings.</summary>
    public async Task SaveSettingsAsync(WebhookSettingsData settings, CancellationToken ct = default)
    {
        using var scope = _services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWebhookSettingsRepository>();
        await repo.SaveAsync(settings, ct);
    }

    /// <summary>Send a test message to every persisted endpoint of the given platform.</summary>
    public Task<WebhookDeliveryReport> TestPlatformAsync(string platform, CancellationToken ct = default)
    {
        var message = new WebhookNotificationMessage
        {
            EventType = "Test",
            Detail = "This is a test notification from Aneiang.Yarp gateway.",
            Severity = "Info"
        };
        return SendToPlatformsAsync(message, [platform], recordHistory: true, ct: ct);
    }

    /// <summary>
    /// Send a test message to the given (possibly unsaved) endpoints without persisting anything.
    /// Used by the inline "test while typing" flow on the notification settings page.
    /// </summary>
    public async Task<WebhookDeliveryReport> TestEndpointsAsync(
        string platform, IReadOnlyList<WebhookEndpointConfig> endpoints, CancellationToken ct = default)
    {
        var report = new WebhookDeliveryReport();
        if (endpoints.Count == 0) return report;

        WebhookSettingsData settings;
        try
        {
            settings = await GetSettingsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load webhook settings for inline test");
            settings = new WebhookSettingsData();
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 3, 60));
        // Inline tests need fast feedback: at most 2 attempts (1 retry).
        var maxAttempts = Math.Min(Math.Clamp(settings.RetryCount, 0, 5) + 1, 2);

        var message = new WebhookNotificationMessage
        {
            EventType = "Test",
            Detail = "This is a test notification from Aneiang.Yarp gateway.",
            Severity = "Info"
        };

        foreach (var endpoint in endpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.Url)) continue;
            report.Total++;

            var detail = await SendWithRetryAsync(platform, endpoint, message, timeout, maxAttempts, ct);
            if (detail.Success) report.Succeeded++;
            report.Details.Add(detail);
            await RecordDeliveryAsync(platform, message, detail, ct);
        }

        report.Success = report.Total > 0 && report.Succeeded == report.Total;
        return report;
    }

    /// <summary>
    /// Deliver a message to all endpoints of the specified platforms.
    /// Retries failed endpoints with exponential backoff and records results.
    /// </summary>
    public async Task<WebhookDeliveryReport> SendToPlatformsAsync(
        WebhookNotificationMessage message,
        IReadOnlyCollection<string> platformKeys,
        bool recordHistory = true,
        CancellationToken ct = default)
    {
        var report = new WebhookDeliveryReport();

        WebhookSettingsData settings;
        try
        {
            settings = await GetSettingsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load webhook settings");
            report.Details.Add(new WebhookDeliveryDetail { Platform = "*", Error = "settings-load-failed" });
            return report;
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 3, 60));
        var maxAttempts = Math.Clamp(settings.RetryCount, 0, 5) + 1;

        foreach (var platformKey in platformKeys)
        {
            if (!settings.Platforms.TryGetValue(platformKey, out var endpoints) || endpoints.Count == 0)
                continue;

            foreach (var endpoint in endpoints)
            {
                if (string.IsNullOrWhiteSpace(endpoint.Url)) continue;
                report.Total++;

                var detail = await SendWithRetryAsync(platformKey, endpoint, message, timeout, maxAttempts, ct);
                if (detail.Success) report.Succeeded++;
                report.Details.Add(detail);

                _logger.LogDebug("Webhook {Platform} -> {Result} after {Attempts} attempt(s) ({Url})",
                    platformKey, detail.Success ? "OK" : detail.Error, detail.Attempts, endpoint.Url);

                if (recordHistory)
                    await RecordDeliveryAsync(platformKey, message, detail, ct);
            }
        }

        report.Success = report.Total > 0 && report.Succeeded == report.Total;
        return report;
    }

    private async Task<WebhookDeliveryDetail> SendWithRetryAsync(
        string platform, WebhookEndpointConfig endpoint, WebhookNotificationMessage message,
        TimeSpan timeout, int maxAttempts, CancellationToken ct)
    {
        var detail = new WebhookDeliveryDetail { Platform = platform, Url = endpoint.Url };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var client = _httpFactory.CreateClient("webhook");
        client.Timeout = Max(timeout, MinSendTimeout);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            detail.Attempts = attempt;
            try
            {
                var payload = BuildPayload(platform, message, endpoint, out var requestUrl);
                using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                using var response = await client.SendAsync(request, ct);
                detail.HttpStatusCode = (int)response.StatusCode;
                detail.Success = response.IsSuccessStatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    detail.Error = Truncate(body, 200);
                }
                else
                {
                    detail.Error = null;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                detail.Success = false;
                detail.Error = "timeout";
            }
            catch (Exception ex)
            {
                detail.Success = false;
                detail.Error = Truncate(ex.Message, 200);
            }

            if (detail.Success || attempt >= maxAttempts) break;

            // Exponential backoff: 1s, 4s, 16s ...
            var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(4, attempt - 1)));
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
        }

        stopwatch.Stop();
        detail.DurationMs = (int)stopwatch.ElapsedMilliseconds;
        return detail;
    }

    private async Task RecordDeliveryAsync(
        string platform, WebhookNotificationMessage message, WebhookDeliveryDetail detail, CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryRecordRepository>();
            await repo.AddAsync(new WebhookDeliveryRecord
            {
                Platform = platform,
                EventType = message.EventType,
                Target = message.Target,
                Success = detail.Success,
                HttpStatusCode = detail.HttpStatusCode,
                Error = detail.Error,
                DurationMs = detail.DurationMs
            }, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record webhook delivery history for {Platform}", platform);
        }
    }

    // ─── Payload builders (2026 platform contracts) ─────────────────────────────

    /// <summary>Build the JSON payload for the given platform. Outputs the request URL (DingTalk sign appended).</summary>
    internal static string BuildPayload(
        string platform, WebhookNotificationMessage message, WebhookEndpointConfig endpoint, out string requestUrl)
    {
        var title = $"[Aneiang.Yarp] {message.EventType}";
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        requestUrl = endpoint.Url;
        var p = platform.ToLowerInvariant();

        if (p == WebhookPlatforms.DingTalk)
        {
            requestUrl = BuildDingTalkSignedUrl(endpoint);
            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["msgtype"] = "markdown",
                ["markdown"] = new Dictionary<string, object?>
                {
                    ["title"] = title,
                    ["text"] = MarkdownBody(title, message, timestamp)
                }
            });
        }

        if (p == WebhookPlatforms.WeChat)
        {
            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["msgtype"] = "markdown",
                ["markdown"] = new Dictionary<string, object?>
                {
                    ["content"] = MarkdownBody(title, message, timestamp)
                }
            });
        }

        if (p == WebhookPlatforms.Feishu)
        {
            var payload = new Dictionary<string, object?>
            {
                ["msg_type"] = "interactive",
                ["card"] = new Dictionary<string, object?>
                {
                    ["config"] = new Dictionary<string, object?> { ["wide_screen_mode"] = true },
                    ["header"] = new Dictionary<string, object?>
                    {
                        ["title"] = new Dictionary<string, object?> { ["tag"] = "plain_text", ["content"] = title },
                        ["template"] = message.Severity switch
                        {
                            "Error" => "red",
                            "Warning" => "orange",
                            _ => "blue"
                        }
                    },
                    ["elements"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["tag"] = "div",
                            ["text"] = new Dictionary<string, object?>
                            {
                                ["tag"] = "lark_md",
                                ["content"] = MarkdownBody(null, message, timestamp)
                            }
                        }
                    }
                }
            };

            // Feishu sign: Base64(HMACSHA256(key = timestamp+"\n"+secret, data = empty))
            if (!string.IsNullOrEmpty(endpoint.Secret))
            {
                var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                payload["timestamp"] = ts;
                payload["sign"] = ComputeFeishuSign(ts, endpoint.Secret!);
            }

            return JsonSerializer.Serialize(payload);
        }

        if (p == WebhookPlatforms.Slack)
        {
            var emoji = message.Severity switch
            {
                "Error" => ":rotating_light:",
                "Warning" => ":warning:",
                _ => ":white_check_mark:"
            };

            var fields = new List<object>
            {
                new Dictionary<string, object?> { ["type"] = "mrkdwn", ["text"] = $"*Event:*\n{message.EventType}" }
            };
            if (!string.IsNullOrEmpty(message.Target))
                fields.Add(new Dictionary<string, object?> { ["type"] = "mrkdwn", ["text"] = $"*Target:*\n{message.Target}" });
            if (!string.IsNullOrEmpty(message.Operator))
                fields.Add(new Dictionary<string, object?> { ["type"] = "mrkdwn", ["text"] = $"*Operator:*\n{message.Operator}" });

            var blocks = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "header",
                    ["text"] = new Dictionary<string, object?> { ["type"] = "plain_text", ["text"] = $"{emoji} {title}" }
                },
                new Dictionary<string, object?> { ["type"] = "section", ["fields"] = fields }
            };
            if (!string.IsNullOrEmpty(message.Detail))
                blocks.Add(new Dictionary<string, object?>
                {
                    ["type"] = "section",
                    ["text"] = new Dictionary<string, object?> { ["type"] = "mrkdwn", ["text"] = message.Detail }
                });
            blocks.Add(new Dictionary<string, object?>
            {
                ["type"] = "context",
                ["elements"] = new object[]
                {
                    new Dictionary<string, object?> { ["type"] = "mrkdwn", ["text"] = $"{message.Severity} · {timestamp}" }
                }
            });

            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["text"] = $"{emoji} {title}",
                ["blocks"] = blocks
            });
        }

        if (p == WebhookPlatforms.Discord)
        {
            var color = message.Severity switch
            {
                "Error" => 0xE74C3C,
                "Warning" => 0xF1C40F,
                _ => 0x2ECC71
            };

            var description = new StringBuilder()
                .Append("**Event**: ").Append(message.EventType);
            if (!string.IsNullOrEmpty(message.Target)) description.Append("\n**Target**: ").Append(message.Target);
            if (!string.IsNullOrEmpty(message.Operator)) description.Append("\n**Operator**: ").Append(message.Operator);
            if (!string.IsNullOrEmpty(message.Detail)) description.Append('\n').Append(message.Detail);

            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["embeds"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["title"] = title,
                        ["description"] = Truncate(description.ToString(), 4000),
                        ["color"] = color,
                        ["footer"] = new Dictionary<string, object?> { ["text"] = $"{message.Severity} · {timestamp}" }
                    }
                }
            });
        }

        if (p == WebhookPlatforms.Telegram)
        {
            var text = new StringBuilder().Append(title);
            if (!string.IsNullOrEmpty(message.Target)) text.Append("\nTarget: ").Append(message.Target);
            if (!string.IsNullOrEmpty(message.Operator)) text.Append("\nOperator: ").Append(message.Operator);
            if (!string.IsNullOrEmpty(message.Detail)) text.Append("\n\n").Append(message.Detail);
            text.Append("\n\n").Append(timestamp);

            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["chat_id"] = endpoint.Secret ?? string.Empty,
                ["text"] = text.ToString()
            });
        }

        if (p == WebhookPlatforms.Teams)
        {
            // Power Automate Workflows (O365 connectors retired 2026-03): Adaptive Card wrapper.
            var facts = new List<object>
            {
                new Dictionary<string, object?> { ["title"] = "Event", ["value"] = message.EventType }
            };
            if (!string.IsNullOrEmpty(message.Target))
                facts.Add(new Dictionary<string, object?> { ["title"] = "Target", ["value"] = message.Target });
            if (!string.IsNullOrEmpty(message.Operator))
                facts.Add(new Dictionary<string, object?> { ["title"] = "Operator", ["value"] = message.Operator });
            facts.Add(new Dictionary<string, object?> { ["title"] = "Severity", ["value"] = message.Severity });

            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["type"] = "message",
                ["attachments"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["contentType"] = "application/vnd.microsoft.card.adaptive",
                        ["content"] = new Dictionary<string, object?>
                        {
                            ["type"] = "AdaptiveCard",
                            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
                            ["version"] = "1.4",
                            ["body"] = new object[]
                            {
                                new Dictionary<string, object?>
                                {
                                    ["type"] = "TextBlock", ["size"] = "Medium", ["weight"] = "Bolder", ["text"] = title
                                },
                                new Dictionary<string, object?> { ["type"] = "FactSet", ["facts"] = facts },
                                !string.IsNullOrEmpty(message.Detail)
                                    ? new Dictionary<string, object?> { ["type"] = "TextBlock", ["text"] = message.Detail, ["wrap"] = true }
                                    : new Dictionary<string, object?> { ["type"] = "TextBlock", ["text"] = string.Empty, ["wrap"] = true },
                                new Dictionary<string, object?>
                                {
                                    ["type"] = "TextBlock", ["text"] = timestamp, ["isSubtle"] = true, ["size"] = "Small"
                                }
                            }
                        }
                    }
                }
            });
        }

        // Generic JSON contract for custom integrations.
        var generic = new Dictionary<string, object?>
        {
            ["source"] = "Aneiang.Yarp.Gateway",
            ["event"] = message.EventType,
            ["title"] = title,
            ["severity"] = message.Severity,
            ["timestamp"] = timestamp
        };
        if (!string.IsNullOrEmpty(message.Target)) generic["target"] = message.Target;
        if (!string.IsNullOrEmpty(message.Operator)) generic["operator"] = message.Operator;
        if (!string.IsNullOrEmpty(message.Detail)) generic["detail"] = message.Detail;
        return JsonSerializer.Serialize(generic);
    }

    private static string MarkdownBody(string? title, WebhookNotificationMessage message, string timestamp)
    {
        var sb = new StringBuilder(256);
        if (title != null) sb.Append("#### ").Append(title).Append("\n\n");
        sb.Append("**Event**: ").Append(message.EventType).Append("\n\n");
        if (!string.IsNullOrEmpty(message.Target)) sb.Append("**Target**: ").Append(message.Target).Append("\n\n");
        if (!string.IsNullOrEmpty(message.Operator)) sb.Append("**Operator**: ").Append(message.Operator).Append("\n\n");
        sb.Append("**Severity**: ").Append(message.Severity);
        if (!string.IsNullOrEmpty(message.Detail)) sb.Append("\n\n").Append(message.Detail);
        sb.Append("\n\n> ").Append(timestamp);
        return sb.ToString();
    }

    /// <summary>
    /// DingTalk robots with a configured secret require an HMAC-SHA256 signature
    /// appended to the URL (timestamp + "\n" + secret as data, secret as key).
    /// </summary>
    private static string BuildDingTalkSignedUrl(WebhookEndpointConfig endpoint)
    {
        if (string.IsNullOrEmpty(endpoint.Secret))
            return endpoint.Url;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var stringToSign = Encoding.UTF8.GetBytes($"{timestamp}\n{endpoint.Secret}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(endpoint.Secret!));
        var sign = Convert.ToBase64String(hmac.ComputeHash(stringToSign));

        var separator = endpoint.Url.Contains('?') ? '&' : '?';
        return $"{endpoint.Url}{separator}timestamp={timestamp}&sign={Uri.EscapeDataString(sign)}";
    }

    /// <summary>
    /// Feishu custom-bot signature: Base64(HMACSHA256(key = (timestamp + "\n" + secret).UTF8, data = empty)).
    /// </summary>
    private static string ComputeFeishuSign(string timestamp, string secret)
    {
        var stringToSign = Encoding.UTF8.GetBytes($"{timestamp}\n{secret}");
        using var hmac = new HMACSHA256(stringToSign);
        return Convert.ToBase64String(hmac.ComputeHash(Array.Empty<byte>()));
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static string Truncate(string? text, int maxLength)
        => string.IsNullOrEmpty(text) ? string.Empty
           : text.Length <= maxLength ? text : text[..maxLength];
}
