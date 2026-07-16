namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>Single chat message from frontend.</summary>
public class ChatMessageDto
{
    public string Role { get; set; } = "user";
    public string? Content { get; set; }
}
