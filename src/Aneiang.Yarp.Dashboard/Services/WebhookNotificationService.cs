using System;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services.Implements;
using Aneiang.Yarp.Dashboard.Services.Webhook;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Sends webhook notifications when gateway configuration changes.
/// Subscribes to <see cref="ConfigChangeAuditLog.OnConfigChanged"/> automatically.
/// Fire-and-forget with single retry on failure.
/// Supports multiple platforms via <see cref="IWebhookProvider"/> pipeline.
/// </summary>
public class WebhookNotificationService
{
    private readonly DashboardOptions _options;
    private readonly ILogger<WebhookNotificationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebhookProvider[] _providers;
    private readonly GenericWebhookProvider _genericProvider;

    public WebhookNotificationService(
        IOptions<DashboardOptions> options,
        ILogger<WebhookNotificationService> logger,
        IHttpClientFactory httpClientFactory,
        IEnumerable<IWebhookProvider> providers)
    {
        _options = options.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;

        // Separate generic provider (empty SupportedHosts = fallback) from platform-specific ones
        var providerList = providers.ToList();
        _genericProvider = providerList.OfType<GenericWebhookProvider>().FirstOrDefault()
                         ?? new GenericWebhookProvider();
        _providers = [.. providerList.Where(p => p is not GenericWebhookProvider)];
    }

    /// <summary>
    /// Subscribe to audit log events. Call this once during DI setup.
    /// </summary>
    public void Subscribe(IConfigChangeAuditLog auditLog)
    {
        auditLog.OnConfigChanged += OnConfigChanged;
        _logger.LogInformation("Webhook notification subscribed to config change audit log");
    }

    private void OnConfigChanged(string eventType, string target, string? operatorName, object? details)
    {
        NotifyConfigChange(eventType, target, operatorName, details);
    }

    /// <summary>
    /// Send a config change notification to all configured webhook URLs.
    /// Automatically selects the correct <see cref="IWebhookProvider"/> based on URL host.
    /// Only sends if the event type is enabled in the configuration.
    /// </summary>
    public void NotifyConfigChange(string eventType, string target, string? operatorName = null, object? details = null)
    {
        var urls = _options.WebhookUrls;
        if (urls == null || urls.Count == 0)
            return;

        // Check if this event type is enabled
        var enabledEvents = _options.WebhookEnabledEvents;
        if (enabledEvents != null && enabledEvents.Count > 0 &&
            !enabledEvents.Any(e => string.Equals(e, eventType, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogDebug("Webhook event '{EventType}' skipped (not in enabled list)", eventType);
            return;
        }

        var payload = new WebhookPayload
        {
            EventType = eventType,
            EventLabel = GetEventLabel(eventType),
            Target = target,
            Operator = operatorName,
            Timestamp = DateTime.Now,
            Details = details,
            GatewayName = Environment.MachineName
        };

        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url))
                continue;

            // Fire and forget
            _ = SendAsync(url, payload);
        }
    }

    private async Task SendAsync(string url, WebhookPayload payload)
    {
        try
        {
            var provider = ResolveProvider(url);
            var secret = ResolveSecret(url, provider);
            var request = provider.BuildRequest(url, payload, secret);

            using var http = _httpClientFactory.CreateClient("webhook");
            http.Timeout = TimeSpan.FromSeconds(10);

            var httpRequest = new HttpRequestMessage(
                new HttpMethod(request.Method), request.Url);

            if (request.Body != null)
            {
                httpRequest.Content = new StringContent(request.Body, System.Text.Encoding.UTF8, request.ContentType);
            }

            foreach (var (key, value) in request.Headers)
            {
                httpRequest.Headers.TryAddWithoutValidation(key, value);
            }

            var response = await http.SendAsync(httpRequest);
            _logger.LogDebug(
                "Webhook sent to {Url} via {Provider}: {StatusCode}",
                url, provider.GetType().Name, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send webhook to {Url}", url);
        }
    }

    private static readonly Dictionary<string, string> _eventLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AddRoute"] = "➕ 路由新增",
        ["UpdateRoute"] = "✏️ 路由更新",
        ["RemoveRoute"] = "🗑️ 路由删除",
        ["AddCluster"] = "➕ 集群新增",
        ["UpdateCluster"] = "✏️ 集群更新",
        ["RemoveCluster"] = "🗑️ 集群删除",
        ["RenameCluster"] = "🔄 集群重命名",
        ["RollbackConfig"] = "⏪ 配置回滚",
        ["test"] = "🔧 测试推送"
    };

    private static string GetEventLabel(string eventType) =>
        _eventLabels.GetValueOrDefault(eventType, eventType);

    private IWebhookProvider ResolveProvider(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return _genericProvider;

        var host = uri.Host;
        foreach (var provider in _providers)
        {
            if (provider.SupportedHosts.Any(h => string.Equals(host, h, StringComparison.OrdinalIgnoreCase)))
                return provider;
        }

        return _genericProvider;
    }

    private string? ResolveSecret(string url, IWebhookProvider provider)
    {
        // Per-URL secret takes priority (webhook-settings.json stores per-endpoint secrets)
        if (_options.WebhookSecrets != null &&
            _options.WebhookSecrets.TryGetValue(url, out var urlSecret) &&
            !string.IsNullOrEmpty(urlSecret))
            return urlSecret;

        // Fallback: per-platform secret
        if (_options.WebhookSecrets != null &&
            _options.WebhookSecrets.TryGetValue(provider.PlatformName, out var platformSecret))
            return platformSecret;

        // Fallback to generic secret
        return _options.WebhookSecret;
    }
}
