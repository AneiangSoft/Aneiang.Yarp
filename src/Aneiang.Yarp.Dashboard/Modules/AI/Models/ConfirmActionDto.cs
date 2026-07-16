namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>Confirm action request DTO.</summary>
public class ConfirmActionDto
{
    public string SessionId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string? Arguments { get; set; }
    public string? CallId { get; set; }
    /// <summary>Current UI locale from the frontend.</summary>
    public string? Locale { get; set; }
}
