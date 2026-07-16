using Aneiang.Yarp.Dashboard.Infrastructure.Exceptions;
using Aneiang.Yarp.Dashboard.Modules.Notification.Models;
using Aneiang.Yarp.Dashboard.Modules.Notification.Services;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Application;

/// <summary>
/// Application service for notification settings, rules, channels, and history.
/// </summary>
public interface INotificationAppService
{
    Task<object> GetSettingsAsync();
    Task SaveSettingsAsync(SaveNotificationSettingsRequest request);
    void TestNotification();
    Task<object> CreateRuleAsync(RuleRequest request);
    Task UpdateRuleAsync(string id, RuleRequest request);
    Task DeleteRuleAsync(string id);
    Task<object> CreateChannelAsync(ChannelRequest request);
    Task UpdateChannelAsync(string id, ChannelRequest request);
    Task DeleteChannelAsync(string id);
    Task<bool> TestChannelAsync(string id);
    Task<object> GetSummaryAsync();
    Task<object> GetHistoryAsync(int page, int pageSize, string? eventType, string? severity, string? dateStart, string? dateEnd);
    Task ClearHistoryAsync();
}

/// <inheritdoc/>
public class NotificationAppService : INotificationAppService
{
    private readonly INotificationRepository _repository;
    private readonly INotificationService _notificationService;

    public NotificationAppService(INotificationRepository repository, INotificationService notificationService)
    {
        _repository = repository;
        _notificationService = notificationService;
    }

    public async Task<object> GetSettingsAsync()
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

        return new NotificationSettingsResponse
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
        };
    }

    public async Task SaveSettingsAsync(SaveNotificationSettingsRequest request)
    {
        if (request.Channels != null)
        {
            foreach (var chReq in request.Channels)
            {
                var channel = new NotificationChannel
                {
                    Id = chReq.Id ?? Guid.NewGuid().ToString("N")[..12],
                    Type = Enum.TryParse<NotificationChannelType>(chReq.Type, true, out var t) ? t : NotificationChannelType.Generic,
                    Name = chReq.Name, Url = chReq.Url, Secret = chReq.Secret, Enabled = chReq.Enabled
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
                    Name = ruleReq.Name, EventTypes = ruleReq.EventTypes ?? [],
                    MinSeverity = NotificationSeverity.Info, ChannelIds = ruleReq.ChannelIds,
                    Enabled = ruleReq.Enabled,
                    CooldownSeconds = ruleReq.CooldownSeconds > 0 ? ruleReq.CooldownSeconds : 300,
                    RecordToHistory = ruleReq.RecordToHistory,
                    TargetRouteIds = ruleReq.TargetRouteIds, TargetClusterIds = ruleReq.TargetClusterIds
                };
                await _repository.SaveRuleAsync(rule);
            }
        }

        if (request.GlobalSettings != null)
        {
            var gs = new NotificationGlobalSettings
            {
                Enabled = request.GlobalSettings.Enabled,
                MaxHistoryRecords = request.GlobalSettings.MaxHistoryRecords > 0 ? request.GlobalSettings.MaxHistoryRecords : 500,
                DefaultTimeoutSeconds = request.GlobalSettings.DefaultTimeoutSeconds > 0 ? request.GlobalSettings.DefaultTimeoutSeconds : 10,
                DefaultRetryCount = request.GlobalSettings.DefaultRetryCount >= 0 ? request.GlobalSettings.DefaultRetryCount : 1
            };
            await _repository.SaveGlobalSettingsAsync(gs);
        }

        _notificationService.InvalidateCache();
    }

    public void TestNotification()
    {
        _notificationService.NotifyCustom("TestNotification", "Test Notification",
            "This is a test notification from the notification center.");
    }

    public async Task<object> CreateRuleAsync(RuleRequest request)
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
        return new RuleResponse
        {
            Id = rule.Id, Name = rule.Name, EventTypes = rule.EventTypes,
            ChannelIds = rule.ChannelIds, Enabled = rule.Enabled,
            CooldownSeconds = rule.CooldownSeconds, RecordToHistory = rule.RecordToHistory,
            CreatedAt = rule.CreatedAt, UpdatedAt = rule.UpdatedAt
        };
    }

    public async Task UpdateRuleAsync(string id, RuleRequest request)
    {
        var existing = await _repository.GetRuleAsync(id)
            ?? throw new NotFoundException("Rule", id);

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
    }

    public async Task DeleteRuleAsync(string id)
    {
        await _repository.DeleteRuleAsync(id);
        _notificationService.InvalidateCache();
    }

    public async Task<object> CreateChannelAsync(ChannelRequest request)
    {
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Type = Enum.TryParse<NotificationChannelType>(request.Type, true, out var t) ? t : NotificationChannelType.Generic,
            Name = request.Name, Url = request.Url, Secret = request.Secret, Enabled = request.Enabled
        };
        await _repository.SaveChannelAsync(channel);
        _notificationService.InvalidateCache();
        return new ChannelResponse
        {
            Id = channel.Id, Type = channel.Type.ToString(), Name = channel.Name,
            Url = channel.Url, HasSecret = !string.IsNullOrEmpty(channel.Secret),
            Secret = channel.Secret, Enabled = channel.Enabled,
            CreatedAt = channel.CreatedAt, UpdatedAt = channel.UpdatedAt
        };
    }

    public async Task UpdateChannelAsync(string id, ChannelRequest request)
    {
        var existing = await _repository.GetChannelAsync(id)
            ?? throw new NotFoundException("Channel", id);

        existing.Type = Enum.TryParse<NotificationChannelType>(request.Type, true, out var t) ? t : existing.Type;
        existing.Name = request.Name;
        existing.Url = request.Url;
        if (!string.IsNullOrEmpty(request.Secret))
            existing.Secret = request.Secret;
        existing.Enabled = request.Enabled;

        await _repository.SaveChannelAsync(existing);
        _notificationService.InvalidateCache();
    }

    public async Task DeleteChannelAsync(string id)
    {
        await _repository.DeleteChannelAsync(id);
        _notificationService.InvalidateCache();
    }

    public Task<bool> TestChannelAsync(string id) => _notificationService.TestChannelAsync(id);

    public async Task<object> GetSummaryAsync()
    {
        var (records, total) = await _repository.GetHistoryAsync(1, 500);
        var channels = await _repository.GetChannelsAsync();
        var rules = await _repository.GetRulesAsync();

        var byEventType = records
            .GroupBy(r => r.EventType)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        return new NotificationSummaryResponse
        {
            Total = total, Unread = 0, ByEventType = byEventType,
            LastEvent = records.FirstOrDefault(),
            ChannelsConfigured = channels.Count,
            RulesActive = rules.Count(r => r.Enabled)
        };
    }

    public async Task<object> GetHistoryAsync(int page, int pageSize, string? eventType, string? severity, string? dateStart, string? dateEnd)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 100;
        if (pageSize > 1000) pageSize = 1000;

        var (records, total) = await _repository.GetHistoryAsync(page, pageSize, eventType, severity, dateStart, dateEnd);
        return new NotificationHistoryResponse { Entries = records, Total = total, Page = page, PageSize = pageSize };
    }

    public async Task ClearHistoryAsync() => await _repository.ClearHistoryAsync();
}
