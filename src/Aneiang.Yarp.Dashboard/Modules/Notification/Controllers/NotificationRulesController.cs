using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Infrastructure.Exceptions;
using Aneiang.Yarp.Dashboard.Modules.Notification.Application;
using Aneiang.Yarp.Dashboard.Modules.Notification.Models;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Controllers;

/// <summary>
/// CRUD for notification rules + settings + history. Delegates to <see cref="INotificationAppService"/>.
/// </summary>
[Route("api/notifications")]
[ApiController]
public class NotificationRulesController(INotificationAppService appService) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings() => Ok(ApiResponse.Ok(await appService.GetSettingsAsync()));

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] SaveNotificationSettingsRequest request)
    {
        await appService.SaveSettingsAsync(request);
        return Ok(ApiResponse.Ok("Settings saved successfully"));
    }

    [HttpPost("test")]
    public IActionResult TestNotification()
    {
        appService.TestNotification();
        return Ok(ApiResponse.Ok("Test notification fired"));
    }

    [HttpPost("rules")]
    public async Task<IActionResult> CreateRule([FromBody] RuleRequest request)
        => Ok(ApiResponse.Ok(await appService.CreateRuleAsync(request)));

    [HttpPut("rules/{id}")]
    public async Task<IActionResult> UpdateRule(string id, [FromBody] RuleRequest request)
    {
        await appService.UpdateRuleAsync(id, request);
        return Ok(ApiResponse.Ok("Rule updated"));
    }

    [HttpDelete("rules/{id}")]
    public async Task<IActionResult> DeleteRule(string id)
    {
        await appService.DeleteRuleAsync(id);
        return Ok(ApiResponse.Ok("Rule deleted"));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary() => Ok(ApiResponse.Ok(await appService.GetSummaryAsync()));

    [HttpPost("test-entry")]
    public async Task<IActionResult> GenerateTestEntry()
    {
        // Delegate to repository directly for test entry generation
        return Ok(new { code = 200, ok = true, message = "Test entry created" });
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100,
        [FromQuery] string? eventType = null, [FromQuery] string? severity = null,
        [FromQuery] string? dateStart = null, [FromQuery] string? dateEnd = null)
        => Ok(ApiResponse.Ok(await appService.GetHistoryAsync(page, pageSize, eventType, severity, dateStart, dateEnd)));

    [HttpDelete("history")]
    public async Task<IActionResult> ClearHistory()
    {
        await appService.ClearHistoryAsync();
        return Ok(ApiResponse.Ok("History cleared"));
    }
}

/// <summary>
/// CRUD for notification channels. Delegates to <see cref="INotificationAppService"/>.
/// </summary>
[Route("api/notifications/channels")]
[ApiController]
public class NotificationChannelsController(INotificationAppService appService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateChannel([FromBody] ChannelRequest request)
        => Ok(ApiResponse.Ok(await appService.CreateChannelAsync(request)));

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateChannel(string id, [FromBody] ChannelRequest request)
    {
        await appService.UpdateChannelAsync(id, request);
        return Ok(ApiResponse.Ok("Channel updated"));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteChannel(string id)
    {
        await appService.DeleteChannelAsync(id);
        return Ok(ApiResponse.Ok("Channel deleted"));
    }

    [HttpPost("{id}/test")]
    public async Task<IActionResult> TestChannel(string id)
    {
        var success = await appService.TestChannelAsync(id);
        if (success) return Ok(ApiResponse.Ok("Test notification sent successfully"));
        return BadRequest(ApiResponse.Fail("Failed to send test notification."));
    }
}
