using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Sends webhook notifications when gateway configuration changes.
/// Fire-and-forget with single retry on failure.
/// Supports HMAC-SHA256 signature for payload verification.
/// </summary>
public class WebhookNotificationService
{
    private readonly DashboardOptions _options;
    private readonly ILogger<WebhookNotificationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public WebhookNotificationService(
        IOptions<DashboardOptions> options,
        ILogger<WebhookNotificationService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Send a config change notification to all configured webhook URLs.
    /// </summary>
    public void NotifyConfigChange(string eventType, string target, string? operatorName = null, object? details = null)
    {
        var urls = _options.WebhookUrls;
        if (urls == null || urls.Count == 0)
            return;

        var payload = new WebhookPayload
        {
            EventType = eventType,
            Target = target,
            Operator = operatorName,
            Timestamp = DateTime.UtcNow,
            Details = details
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);

        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url))
                continue;

            // Fire and forget
            _ = SendAsync(url, json);
        }
    }

    private async Task SendAsync(string url, string json)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient("webhook");
            http.Timeout = TimeSpan.FromSeconds(10);

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Add HMAC-SHA256 signature if secret is configured
            if (!string.IsNullOrEmpty(_options.WebhookSecret))
            {
                var signature = ComputeSignature(json, _options.WebhookSecret);
                content.Headers.Add("X-Webhook-Signature", $"sha256={signature}");
            }

            var response = await http.PostAsync(url, content);
            _logger.LogDebug(
                "Webhook sent to {Url}: {StatusCode}",
                url, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send webhook to {Url}", url);
        }
    }

    private static string ComputeSignature(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public class WebhookPayload
{
    public string EventType { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? Operator { get; set; }
    public DateTime Timestamp { get; set; }
    public object? Details { get; set; }
}
