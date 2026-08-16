using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Notifications;

/// <summary>Outcome of a single webhook endpoint delivery attempt.</summary>
public sealed class WebhookDeliveryDetail
{
    public string Platform { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? Error { get; set; }
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
/// Sends webhook notifications (DingTalk robot with HMAC-SHA256 signing,
/// plus a generic JSON contract) when subscribed config-change events occur.
/// Settings are persisted via <see cref="IWebhookSettingsRepository"/>.
/// </summary>
public sealed class WebhookNotificationService
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

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

    /// <summary>Send a test message to every endpoint of the given platform.</summary>
    public Task<WebhookDeliveryReport> TestPlatformAsync(string platform, CancellationToken ct = default)
        => SendCoreAsync(platform, eventType: "Test", target: null, operatorName: null, forceSend: true, ct: ct);

    /// <summary>
    /// Notify all platforms about a config-change event.
    /// Silently no-ops when the event is not subscribed or no endpoints are configured.
    /// </summary>
    public Task<WebhookDeliveryReport> NotifyConfigChangeAsync(
        string eventType, string? target, string? operatorName, CancellationToken ct = default)
        => SendCoreAsync(platform: null, eventType, target, operatorName, forceSend: false, ct: ct);

    private async Task<WebhookDeliveryReport> SendCoreAsync(
        string? platform, string eventType, string? target, string? operatorName, bool forceSend, CancellationToken ct)
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

        if (!forceSend && !settings.EnabledEvents.Contains(eventType, StringComparer.OrdinalIgnoreCase))
            return report; // Event not subscribed - nothing to do.

        var platformsToSend = platform is null
            ? settings.Platforms
            : settings.Platforms
                .Where(kv => kv.Key.Equals(platform, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        var title = $"[Aneiang.Yarp] {eventType}";
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        var client = _httpFactory.CreateClient("webhook");
        client.Timeout = SendTimeout;

        foreach (var (platformKey, endpoints) in platformsToSend)
        {
            foreach (var endpoint in endpoints)
            {
                if (string.IsNullOrWhiteSpace(endpoint.Url)) continue;
                report.Total++;

                var detail = new WebhookDeliveryDetail { Platform = platformKey, Url = endpoint.Url };
                try
                {
                    var payload = BuildPayload(platformKey, eventType, title, target, operatorName, timestamp);
                    using var request = new HttpRequestMessage(HttpMethod.Post, BuildSignedUrl(platformKey, endpoint))
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
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    detail.Error = "timeout";
                }
                catch (Exception ex)
                {
                    detail.Success = false;
                    detail.Error = Truncate(ex.Message, 200);
                }

                if (detail.Success) report.Succeeded++;
                report.Details.Add(detail);

                _logger.LogDebug("Webhook {Platform} -> {Result} ({Url})",
                    platformKey, detail.Success ? "OK" : detail.Error, endpoint.Url);
            }
        }

        report.Success = report.Total > 0 && report.Succeeded == report.Total;
        return report;
    }

    private static string BuildPayload(
        string platform, string eventType, string title, string? target, string? operatorName, string timestamp)
    {
        var sb = new StringBuilder(256);
        switch (platform.ToLowerInvariant())
        {
            case "dingtalk":
            case "wechat":
            case "feishu":
                // Markdown message (supported by all three major Chinese platforms).
                sb.Append("{\"msgtype\":\"markdown\",\"markdown\":{\"title\":")
                  .Append(JsonSerializer.Serialize(title))
                  .Append(",\"text\":\"#### ")
                  .Append(EscapeMarkdown(title))
                  .Append("\\n\\n**Event**: ")
                  .Append(EscapeMarkdown(eventType));
                if (!string.IsNullOrEmpty(target)) sb.Append("\\n\\n**Target**: ").Append(EscapeMarkdown(target));
                if (!string.IsNullOrEmpty(operatorName)) sb.Append("\\n\\n**Operator**: ").Append(EscapeMarkdown(operatorName));
                sb.Append("\\n\\n> ").Append(timestamp).Append("\"}}");
                return sb.ToString();

            default:
                // Generic JSON contract for custom integrations.
                sb.Append("{\"source\":\"Aneiang.Yarp.Gateway\",\"event\":")
                  .Append(JsonSerializer.Serialize(eventType))
                  .Append(",\"title\":")
                  .Append(JsonSerializer.Serialize(title));
                if (!string.IsNullOrEmpty(target)) sb.Append(",\"target\":").Append(JsonSerializer.Serialize(target));
                if (!string.IsNullOrEmpty(operatorName)) sb.Append(",\"operator\":").Append(JsonSerializer.Serialize(operatorName));
                sb.Append(",\"timestamp\":\"").Append(timestamp).Append("\"}");
                return sb.ToString();
        }
    }

    /// <summary>
    /// DingTalk robots with a configured secret require an HMAC-SHA256 signature
    /// appended to the URL (timestamp + "\n" + secret).
    /// </summary>
    private static string BuildSignedUrl(string platform, WebhookEndpointConfig endpoint)
    {
        if (!platform.Equals("dingtalk", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(endpoint.Secret))
            return endpoint.Url;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var stringToSign = Encoding.UTF8.GetBytes($"{timestamp}\n{endpoint.Secret}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(endpoint.Secret!));
        var sign = Convert.ToBase64String(hmac.ComputeHash(stringToSign));

        var separator = endpoint.Url.Contains('?') ? '&' : '?';
        return $"{endpoint.Url}{separator}timestamp={timestamp}&sign={Uri.EscapeDataString(sign)}";
    }

    private static string EscapeMarkdown(string text)
        => text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");

    private static string Truncate(string? text, int maxLength)
        => string.IsNullOrEmpty(text) ? string.Empty
           : text.Length <= maxLength ? text : text[..maxLength];
}
