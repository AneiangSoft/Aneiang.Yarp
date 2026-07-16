using System.Text.Json.Serialization;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Models;

/// <summary>
/// Paginated response DTO for notification history entries.
/// </summary>
public class NotificationHistoryResponse
{
    [JsonPropertyName("entries")]
    public List<NotificationHistory> Entries { get; set; } = [];

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; } = 100;
}
