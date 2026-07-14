using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Dashboard.Modules.Notification.Services;
using Aneiang.Yarp.Dashboard.Modules.Notification.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Controllers;

/// <summary>
/// CRUD for notification rules + settings + history. Split from the original monolithic controller.
/// </summary>
[Route("api/notifications")]
[ApiController]
public class NotificationRulesController : ControllerBase
{
    private readonly INotificationRepository _repository;
    private readonly INotificationService _notificationService;
    private readonly ILogger<NotificationRulesController> _logger;

    public NotificationRulesController(
        INotificationRepository repository,
        INotificationService notificationService,
        ILogger<NotificationRulesController> logger)
    {
        _repository = repository;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>Get all notification settings.</summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var channels = await _repository.GetChannelsAsync();
        var rules = await _repository.GetRulesAsync();
        var globalSettings = await _repository.GetGlobalSettingsAsync();

        var channelResponses = channels.Select(c => new ChannelResponse
        {
            Id = c.Id, Type = c.Type.ToString(), Name = c.Name, Url = c.Url,
            HasSecret = !string.IsNullOrEmpty(c.Secret), Secret = c.Secret,
            Enabled = c.Enabled, CreatedAt = c.CreatedAt, UpdatedAt = c.UpdatedAt
        }).ToList();

        var channelMap = channels.ToDictionary(c => c.Id);
        var ruleResponses = rules.Select(r => new RuleResponse
        {
            Id = r.Id, Name = r.Name, EventTypes = r.EventTypes,
            ChannelIds = r.ChannelIds, Enabled = r.Enabled,
            CooldownSeconds = r.CooldownSeconds, RecordToHistory = r.RecordToHistory,
            TargetRouteIds = r.TargetRouteIds, TargetClusterIds = r.TargetClusterIds,
            ChannelDetails = r.ChannelIds
                .Select(id => channelMap.TryGetValue(id, out var ch) ? new ChannelResponse
                {
                    Id = ch.Id, Type = ch.Type.ToString(), Name = ch.Name, Url = ch.Url,
                    HasSecret = !string.IsNullOrEmpty(ch.Secret), Enabled = ch.Enabled,
                    CreatedAt = ch.CreatedAt, UpdatedAt = ch.UpdatedAt
                } : null)
                .Where(c => c != null).Cast<ChannelResponse>().ToList(),
            CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt
        }).ToList();

        return Ok(new
        {
            code = 200,
            data = new NotificationSettingsResponse
            {
                Channels = channelResponses,
                Rules = ruleResponses,
                GlobalSettings = new GlobalSettingsResponse
                {
                    Enabled = globalSettings.Enabled,
                    MaxHistoryRecords = globalSettings.MaxHistoryRecords,
                    DefaultTimeoutSeconds = globalSettings.DefaultTimeoutSeconds,
                    DefaultRetryCount = globalSettings.DefaultRetryCount
                }
            }
        });
    }

    /// <summary>Save notification settings.</summary>
    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] SaveNotificationSettingsRequest request)
    {
        try
        {
            if (request.Channels != null)
            {
                foreach (var chReq in request.Channels)
                {
                    var channel = new NotificationChannel
                    {
                        Id = chReq.Id ?? Guid.NewGuid().ToString("N")[..12],
                        Type = Enum.TryParse<NotificationChannelType>(chReq.Type, true, out var t)
                            ? t : NotificationChannelType.Generic,
                        Name = chReq.Name, Url = chReq.Url,
                        Secret = chReq.Secret, Enabled = chReq.Enabled
                    };
                    await _repository.SaveChannelAsync(channel);
                }
            }

            if (request.Rules != null)
            {
                foreach (var ruleReq in request.Rules)
                {
                    var rule = new NotificationRule
                    {
                        Id = ruleReq.Id ?? Guid.NewGuid().ToString("N")[..12],
                        Name = ruleReq.Name,
                        EventTypes = ruleReq.EventTypes ?? [],
                        MinSeverity = NotificationSeverity.Info,
                        ChannelIds = ruleReq.ChannelIds,
                        Enabled = ruleReq.Enabled,
                        CooldownSeconds = ruleReq.CooldownSeconds > 0 ? ruleReq.CooldownSeconds : 300,
                        RecordToHistory = ruleReq.RecordToHistory,
                        TargetRouteIds = ruleReq.TargetRouteIds,
                        TargetClusterIds = ruleReq.TargetClusterIds
                    };
                    await _repository.SaveRuleAsync(rule);
                }
            }

            if (request.GlobalSettings != null)
            {
                var globalSettings = new NotificationGlobalSettings
                {
                    Enabled = request.GlobalSettings.Enabled,
                    MaxHistoryRecords = request.GlobalSettings.MaxHistoryRecords > 0 ? request.GlobalSettings.MaxHistoryRecords : 500,
                    DefaultTimeoutSeconds = request.GlobalSettings.DefaultTimeoutSeconds > 0 ? request.GlobalSettings.DefaultTimeoutSeconds : 10,
                    DefaultRetryCount = request.GlobalSettings.DefaultRetryCount >= 0 ? request.GlobalSettings.DefaultRetryCount : 1
                };
                await _repository.SaveGlobalSettingsAsync(globalSettings);
            }

            _notificationService.InvalidateCache();
            _logger.LogDebug("Notification settings saved");
            return Ok(new { code = 200, message = "Settings saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save notification settings");
            return StatusCode(500, new { code = 500, message = "Failed to save settings: " + ex.Message });
        }
    }

    /// <summary>Send a test notification.</summary>
    [HttpPost("test")]
    public IActionResult TestNotification()
    {
        _notificationService.NotifyCustom("TestNotification", "Test Notification",
            "This is a test notification from the notification center.");
        return Ok(new { code = 200, message = "Test notification fired" });
    }

    /// <summary>Create a new rule.</summary>
    [HttpPost("rules")]
    public async Task<IActionResult> CreateRule([FromBody] RuleRequest request)
    {
        var rule = new NotificationRule
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = request.Name, EventTypes = request.EventTypes ?? [],
            MinSeverity = NotificationSeverity.Info, ChannelIds = request.ChannelIds,
            Enabled = request.Enabled,
            CooldownSeconds = request.CooldownSeconds > 0 ? request.CooldownSeconds : 300,
            RecordToHistory = request.RecordToHistory,
            TargetRouteIds = request.TargetRouteIds, TargetClusterIds = request.TargetClusterIds
        };

        await _repository.SaveRuleAsync(rule);
        _notificationService.InvalidateCache();

        return Ok(new
        {
            code = 200,
            data = new RuleResponse
            {
                Id = rule.Id, Name = rule.Name, EventTypes = rule.EventTypes,
                ChannelIds = rule.ChannelIds, Enabled = rule.Enabled,
                CooldownSeconds = rule.CooldownSeconds, RecordToHistory = rule.RecordToHistory,
                CreatedAt = rule.CreatedAt, UpdatedAt = rule.UpdatedAt
            }
        });
    }

    /// <summary>Update an existing rule.</summary>
    [HttpPut("rules/{id}")]
    public async Task<IActionResult> UpdateRule(string id, [FromBody] RuleRequest request)
    {
        var existing = await _repository.GetRuleAsync(id);
        if (existing == null)
            return NotFound(new { code = 404, message = "Rule not found" });

        existing.Name = request.Name;
        existing.EventTypes = request.EventTypes ?? [];
        existing.MinSeverity = NotificationSeverity.Info;
        existing.ChannelIds = request.ChannelIds;
        existing.Enabled = request.Enabled;
        existing.CooldownSeconds = request.CooldownSeconds > 0 ? request.CooldownSeconds : 300;
        existing.RecordToHistory = request.RecordToHistory;
        existing.TargetRouteIds = request.TargetRouteIds;
        existing.TargetClusterIds = request.TargetClusterIds;

        await _repository.SaveRuleAsync(existing);
        _notificationService.InvalidateCache();

        return Ok(new { code = 200, message = "Rule updated" });
    }

    /// <summary>Delete a rule.</summary>
    [HttpDelete("rules/{id}")]
    public async Task<IActionResult> DeleteRule(string id)
    {
        await _repository.DeleteRuleAsync(id);
        _notificationService.InvalidateCache();
        return Ok(new { code = 200, message = "Rule deleted" });
    }

    /// <summary>Get notification summary statistics.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var (records, total) = await _repository.GetHistoryAsync(1, 500);
        var channels = await _repository.GetChannelsAsync();
        var rules = await _repository.GetRulesAsync();

        var byEventType = records
            .GroupBy(r => r.EventType)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        return Ok(new
        {
            code = 200,
            data = new NotificationSummaryResponse
            {
                Total = total, Unread = 0, ByEventType = byEventType,
                LastEvent = records.FirstOrDefault(),
                ChannelsConfigured = channels.Count,
                RulesActive = rules.Count(r => r.Enabled)
            }
        });
    }

    /// <summary>Generate a test notification history entry directly.</summary>
    [HttpPost("test-entry")]
    public async Task<IActionResult> GenerateTestEntry()
    {
        try
        {
            var entry = new NotificationHistory
            {
                Id = Guid.NewGuid().ToString("N")[..16],
                EventType = "UpdateRoute",
                Severity = NotificationSeverity.Info,
                Title = "测试通知记录",
                Message = $"这是一条直接生成的测试记录，生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                Timestamp = DateTime.Now,
                ClusterId = "test-cluster", RouteId = "test-route", ClientIp = "127.0.0.1",
                NotifiedChannels = new List<string> { "测试渠道" },
                DeliverySuccess = true
            };

            await _repository.RecordNotificationAsync(entry);
            _logger.LogDebug("[Notification] Test history entry generated: {Id}", entry.Id);
            return Ok(new { code = 200, ok = true, message = "Test entry created", entry });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Notification] Failed to create test history entry");
            return StatusCode(500, new { code = 500, ok = false, error = "Failed to create test entry: " + ex.Message });
        }
    }

    /// <summary>Get notification history.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100,
        [FromQuery] string? eventType = null, [FromQuery] string? severity = null,
        [FromQuery] string? dateStart = null, [FromQuery] string? dateEnd = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 100;
        if (pageSize > 1000) pageSize = 1000;

        var (records, total) = await _repository.GetHistoryAsync(page, pageSize, eventType, severity, dateStart, dateEnd);

        return Ok(new
        {
            code = 200,
            data = new NotificationHistoryResponse
            {
                Entries = records, Total = total, Page = page, PageSize = pageSize
            }
        });
    }

    /// <summary>Clear notification history.</summary>
    [HttpDelete("history")]
    public async Task<IActionResult> ClearHistory()
    {
        await _repository.ClearHistoryAsync();
        return Ok(new { code = 200, message = "History cleared" });
    }
}
