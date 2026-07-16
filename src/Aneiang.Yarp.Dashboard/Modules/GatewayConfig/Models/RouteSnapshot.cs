namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

/// <summary>Route data captured in a config snapshot.</summary>
public class RouteSnapshot
{
    public string RouteId { get; set; } = string.Empty;
    public string ClusterId { get; set; } = string.Empty;
    public string? MatchPath { get; set; }
    public int Order { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}
