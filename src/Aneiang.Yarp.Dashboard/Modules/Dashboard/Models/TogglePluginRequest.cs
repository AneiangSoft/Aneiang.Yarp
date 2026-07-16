namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Request model for toggling a plugin's enabled state.
/// </summary>
public class TogglePluginRequest
{
    public bool Enabled { get; set; }
}
