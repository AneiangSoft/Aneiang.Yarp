using Aneiang.Yarp.Dashboard.Infrastructure.Notifications;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Webhook notification settings API consumed by the notification management page:
/// GET/POST <c>api/webhook/settings</c>, POST <c>api/webhook/test</c>,
/// GET <c>api/webhook/history</c>, DELETE <c>api/webhook/history</c>,
/// POST <c>api/webhook/cooldown/clear</c>.
/// </summary>
[Route("api/webhook")]
[ApiController]
public sealed class WebhookSettingsController : ControllerBase
{
    private const int MaxEndpointsPerPlatform = 5;

    private readonly WebhookNotificationService _webhooks;
    private readonly NotificationDispatcher _dispatcher;
    private readonly IServiceProvider _services;
    private readonly ILogger<WebhookSettingsController> _logger;

    public WebhookSettingsController(
        WebhookNotificationService webhooks,
        NotificationDispatcher dispatcher,
        IServiceProvider services,
        ILogger<WebhookSettingsController> logger)
    {
        _webhooks = webhooks;
        _dispatcher = dispatcher;
        _services = services;
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
        // Always include known platforms so the UI can render empty cards.
        foreach (var platform in WebhookPlatforms.All)
            data.TryAdd(platform, Array.Empty<object>());

        // Preserve unknown custom platform keys as endpoints too.
        data["enabledEvents"] = settings.EnabledEvents;

        var platformEvents = new Dictionary<string, object>();
        foreach (var (platform, events) in settings.PlatformEvents)
            platformEvents[platform] = events;
        data["platformEvents"] = platformEvents;

        data["cooldownSeconds"] = settings.CooldownSeconds;
        data["retryCount"] = settings.RetryCount;
        data["timeoutSeconds"] = settings.TimeoutSeconds;

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
                .Select(e => e.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            CooldownSeconds = Math.Clamp(request.CooldownSeconds ?? 60, 0, 3600),
            RetryCount = Math.Clamp(request.RetryCount ?? 2, 0, 5),
            TimeoutSeconds = Math.Clamp(request.TimeoutSeconds ?? 10, 3, 60)
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

        foreach (var (platform, events) in request.PlatformEvents ?? new Dictionary<string, List<string>>())
        {
            if (string.IsNullOrWhiteSpace(platform)) continue;
            // Empty lists are meaningful: an explicit "subscribe to nothing" that
            // overrides the global fallback (EnabledEvents).
            settings.PlatformEvents[platform.Trim()] = (events ?? [])
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        await _webhooks.SaveSettingsAsync(settings, ct);
        _logger.LogInformation(
            "Webhook settings saved: {PlatformCount} platform(s), {EventCount} global event(s), {PlatformEventCount} platform subscription(s)",
            settings.Platforms.Count, settings.EnabledEvents.Count, settings.PlatformEvents.Count);

        return Ok(new { code = 200, data = new { saved = true } });
    }

    // POST api/webhook/platform
    // Saves a single platform's endpoints (per-platform save on the notification page).
    // Other settings (events, cooldown, other platforms) are preserved untouched.
    [HttpPost("platform")]
    public async Task<IActionResult> SavePlatform([FromBody] WebhookPlatformSaveRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Platform))
            return BadRequest(new { code = 400, message = "platform is required" });

        var platform = request.Platform.Trim();
        if (!WebhookPlatforms.IsKnown(platform))
            return BadRequest(new { code = 400, message = "unknown platform" });

        var endpoints = (request.Endpoints ?? [])
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

        var settings = await _webhooks.GetSettingsAsync(ct);
        if (endpoints.Count == 0)
            settings.Platforms.Remove(platform);
        else
            settings.Platforms[platform] = endpoints;

        await _webhooks.SaveSettingsAsync(settings, ct);
        _logger.LogInformation(
            "Webhook platform '{Platform}' saved: {EndpointCount} endpoint(s)", platform, endpoints.Count);

        return Ok(new { code = 200, data = new { saved = true, platform, endpoints = endpoints.Count } });
    }

    // POST api/webhook/test
    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] WebhookTestRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Platform))
            return BadRequest(new { code = 400, message = "platform is required" });

        var platform = request.Platform.Trim();

        WebhookDeliveryReport report;
        if (request.Endpoints is { Count: > 0 })
        {
            // Inline test: verify endpoints staged in the UI (possibly unsaved) before persisting.
            var endpoints = request.Endpoints
                .Where(e => !string.IsNullOrWhiteSpace(e.Url))
                .Select(e => new WebhookEndpointConfig
                {
                    Url = e.Url!.Trim(),
                    Secret = string.IsNullOrWhiteSpace(e.Secret) ? null : e.Secret!.Trim()
                })
                .GroupBy(e => e.Url, StringComparer.OrdinalIgnoreCase) // dedupe
                .Select(g => g.First())
                .Take(MaxEndpointsPerPlatform)
                .ToList();

            report = await _webhooks.TestEndpointsAsync(platform, endpoints, ct);
        }
        else
        {
            report = await _webhooks.TestPlatformAsync(platform, ct);
        }

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
                    d.Error,
                    d.Attempts,
                    d.DurationMs
                }).ToList()
            }
        });
    }

    // GET api/webhook/history?limit=200
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int limit = 200, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 1000);
        using var scope = _services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryRecordRepository>();
        var records = await repo.GetRecentAsync(limit, ct);

        return Ok(new
        {
            code = 200,
            data = records.Select(r => new
            {
                r.Id,
                r.Platform,
                eventType = r.EventType,
                r.Target,
                r.Success,
                httpStatus = r.HttpStatusCode,
                r.Error,
                r.DurationMs,
                createdAt = r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            })
        });
    }

    // DELETE api/webhook/history
    [HttpDelete("history")]
    public async Task<IActionResult> ClearHistory(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryRecordRepository>();
        await repo.ClearAsync(ct);
        return Ok(new { code = 200, data = new { cleared = true } });
    }

    // POST api/webhook/cooldown/clear
    [HttpPost("cooldown/clear")]
    public IActionResult ClearCooldown()
    {
        _dispatcher.ClearCooldown();
        return Ok(new { code = 200, data = new { cleared = true } });
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
    public Dictionary<string, List<string>>? PlatformEvents { get; set; }
    public int? CooldownSeconds { get; set; }
    public int? RetryCount { get; set; }
    public int? TimeoutSeconds { get; set; }
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

    /// <summary>Optional staged endpoints (url + secret) to test before saving.</summary>
    public List<WebhookEndpointInput>? Endpoints { get; set; }
}

public sealed class WebhookPlatformSaveRequest
{
    public string? Platform { get; set; }
    public List<WebhookEndpointInput>? Endpoints { get; set; }
}
