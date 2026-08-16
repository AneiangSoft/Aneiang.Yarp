using Aneiang.Yarp.Dashboard.Infrastructure.Notifications;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Webhook notification settings API consumed by the Dashboard config page:
/// GET/POST <c>api/webhook/settings</c> and POST <c>api/webhook/test</c>.
/// </summary>
[Route("api/webhook")]
[ApiController]
public sealed class WebhookSettingsController : ControllerBase
{
    /// <summary>Known platforms rendered as tabs in the settings UI.</summary>
    private static readonly string[] KnownPlatforms = ["dingtalk", "wechat", "feishu", "generic"];

    private const int MaxEndpointsPerPlatform = 5;

    private readonly WebhookNotificationService _webhooks;
    private readonly ILogger<WebhookSettingsController> _logger;

    public WebhookSettingsController(WebhookNotificationService webhooks, ILogger<WebhookSettingsController> logger)
    {
        _webhooks = webhooks;
        _logger = logger;
    }

    // GET api/webhook/settings
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var settings = await _webhooks.GetSettingsAsync(ct);

        // Frontend contract: each platform key maps directly to an endpoint array.
        var data = new Dictionary<string, object>();
        foreach (var (platform, endpoints) in settings.Platforms)
        {
            data[platform] = endpoints
                .Select(e => new { url = e.Url, secret = e.Secret })
                .ToList();
        }
        // Always include known platforms so the UI can render empty tabs.
        foreach (var platform in KnownPlatforms)
            data.TryAdd(platform, Array.Empty<object>());

        data["enabledEvents"] = settings.EnabledEvents;

        return Ok(new { code = 200, data });
    }

    // POST api/webhook/settings
    [HttpPost("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] WebhookSettingsSaveRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { code = 400, message = "Invalid request body" });

        var settings = new WebhookSettingsData
        {
            EnabledEvents = (request.EnabledEvents ?? [])
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };

        foreach (var (platform, platformInput) in request.Platforms ?? new Dictionary<string, WebhookPlatformInput>())
        {
            if (string.IsNullOrWhiteSpace(platform)) continue;

            var endpoints = (platformInput?.Endpoints ?? [])
                .Select(e => new WebhookEndpointConfig
                {
                    Url = (e.Url ?? string.Empty).Trim(),
                    Secret = string.IsNullOrWhiteSpace(e.Secret) ? null : e.Secret!.Trim()
                })
                .Where(e => e.Url.Length > 0)
                .GroupBy(e => e.Url, StringComparer.OrdinalIgnoreCase) // dedupe
                .Select(g => g.First())
                .Take(MaxEndpointsPerPlatform)
                .ToList();

            if (endpoints.Count > 0)
                settings.Platforms[platform.Trim()] = endpoints;
        }

        await _webhooks.SaveSettingsAsync(settings, ct);
        _logger.LogInformation("Webhook settings saved: {PlatformCount} platform(s), {EventCount} event(s) subscribed",
            settings.Platforms.Count, settings.EnabledEvents.Count);

        return Ok(new { code = 200, data = new { saved = true } });
    }

    // POST api/webhook/test
    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] WebhookTestRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Platform))
            return BadRequest(new { code = 400, message = "platform is required" });

        var report = await _webhooks.TestPlatformAsync(request.Platform.Trim(), ct);

        return Ok(new
        {
            code = 200,
            data = new
            {
                report.Success,
                report.Total,
                report.Succeeded,
                details = report.Details.Select(d => new
                {
                    d.Platform,
                    url = MaskUrl(d.Url),
                    d.Success,
                    d.Error
                }).ToList()
            }
        });
    }

    /// <summary>Hide access tokens / secrets in endpoint URLs before echoing them to the client.</summary>
    private static string MaskUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        try
        {
            // Mask the value of common token-bearing query parameters.
            return System.Text.RegularExpressions.Regex.Replace(
                url,
                "(?<=[?&](?:access_token|token|key|secret)=)[^&]+",
                "***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        catch
        {
            return url;
        }
    }
}

// ─── Request DTOs ────────────────────────────────────────────────────────────

public sealed class WebhookSettingsSaveRequest
{
    public Dictionary<string, WebhookPlatformInput>? Platforms { get; set; }
    public List<string>? EnabledEvents { get; set; }
}

public sealed class WebhookPlatformInput
{
    public List<WebhookEndpointInput>? Endpoints { get; set; }
}

public sealed class WebhookEndpointInput
{
    public string? Url { get; set; }
    public string? Secret { get; set; }
}

public sealed class WebhookTestRequest
{
    public string? Platform { get; set; }
}
