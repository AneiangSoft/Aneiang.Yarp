namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>Chat request DTO from frontend.</summary>
public class ChatRequestDto
{
    public string? SessionId { get; set; }
    public List<ChatMessageDto>? Messages { get; set; }
    /// <summary>Current UI locale (e.g. "en-US", "zh-CN") from the frontend.</summary>
    public string? Locale { get; set; }
}
