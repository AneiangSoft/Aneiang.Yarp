namespace Aneiang.Yarp.Storage;

public class AIConversationEntry
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public string? FunctionCalls { get; set; }
    public string? ToolCallId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
