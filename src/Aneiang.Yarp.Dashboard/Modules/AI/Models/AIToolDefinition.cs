namespace Aneiang.Yarp.Dashboard.Modules.AI;

/// <summary>
/// Definition of a tool available for AI function calling.
/// Uses OpenAI tools/function-calling standard format.
/// </summary>
public class AIToolDefinition
{
    /// <summary>Tool name (e.g. "get_routes", "create_route").</summary>
    public string Name { get; set; } = "";

    /// <summary>Human-readable description for the AI.</summary>
    public string Description { get; set; } = "";

    /// <summary>JSON Schema describing the tool parameters.</summary>
    public object Parameters { get; set; } = new { type = "object", properties = new { }, required = Array.Empty<string>() };

    /// <summary>Whether this tool is read-only (safe to auto-execute without user confirmation).</summary>
    public bool IsReadOnly { get; set; }
}
