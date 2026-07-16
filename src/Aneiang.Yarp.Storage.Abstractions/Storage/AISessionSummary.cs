namespace Aneiang.Yarp.Storage;

public class AISessionSummary
{
    public string SessionId { get; set; } = "";
    public int MessageCount { get; set; }
    public DateTime LastActivity { get; set; }
}
