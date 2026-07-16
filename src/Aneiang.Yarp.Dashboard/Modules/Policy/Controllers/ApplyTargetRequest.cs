namespace Aneiang.Yarp.Dashboard.Modules.Policy.Controllers;

/// <summary>Request body for apply/unapply operations.</summary>
public class ApplyTargetRequest
{
    /// <summary>Route ID or Cluster ID to apply/unapply the policy to.</summary>
    public string TargetId { get; set; } = string.Empty;
}
