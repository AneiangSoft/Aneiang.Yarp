using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Dashboard.Modules.Notification.Services;
using Aneiang.Yarp.Dashboard.Modules.Notification.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Controllers;

/// <summary>
/// CRUD for notification channels. Split from the original monolithic controller.
/// </summary>
[Route("api/notifications/channels")]
[ApiController]
public class NotificationChannelsController : ControllerBase
{
    private readonly INotificationRepository _repository;
    private readonly INotificationService _notificationService;
    private readonly ILogger<NotificationChannelsController> _logger;

    public NotificationChannelsController(
        INotificationRepository repository,
        INotificationService notificationService,
        ILogger<NotificationChannelsController> logger)
    {
        _repository = repository;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>Create a new channel.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateChannel([FromBody] ChannelRequest request)
    {
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Type = Enum.TryParse<NotificationChannelType>(request.Type, true, out var t)
                ? t : NotificationChannelType.Generic,
            Name = request.Name,
            Url = request.Url,
            Secret = request.Secret,
            Enabled = request.Enabled
        };

        await _repository.SaveChannelAsync(channel);
        _notificationService.InvalidateCache();

        return Ok(new
        {
            code = 200,
            data = new ChannelResponse
            {
                Id = channel.Id,
                Type = channel.Type.ToString(),
                Name = channel.Name,
                Url = channel.Url,
                HasSecret = !string.IsNullOrEmpty(channel.Secret),
                Secret = channel.Secret,
                Enabled = channel.Enabled,
                CreatedAt = channel.CreatedAt,
                UpdatedAt = channel.UpdatedAt
            }
        });
    }

    /// <summary>Update an existing channel.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateChannel(string id, [FromBody] ChannelRequest request)
    {
        var existing = await _repository.GetChannelAsync(id);
        if (existing == null)
            return NotFound(new { code = 404, message = "Channel not found" });

        existing.Type = Enum.TryParse<NotificationChannelType>(request.Type, true, out var t)
            ? t : existing.Type;
        existing.Name = request.Name;
        existing.Url = request.Url;
        if (!string.IsNullOrEmpty(request.Secret))
            existing.Secret = request.Secret;
        existing.Enabled = request.Enabled;

        await _repository.SaveChannelAsync(existing);
        _notificationService.InvalidateCache();

        return Ok(new { code = 200, message = "Channel updated" });
    }

    /// <summary>Delete a channel.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteChannel(string id)
    {
        await _repository.DeleteChannelAsync(id);
        _notificationService.InvalidateCache();
        return Ok(new { code = 200, message = "Channel deleted" });
    }

    /// <summary>Test a channel.</summary>
    [HttpPost("{id}/test")]
    public async Task<IActionResult> TestChannel(string id)
    {
        var success = await _notificationService.TestChannelAsync(id);
        if (success)
            return Ok(new { code = 200, message = "Test notification sent successfully" });
        return BadRequest(new { code = 400, message = "Failed to send test notification." });
    }
}
