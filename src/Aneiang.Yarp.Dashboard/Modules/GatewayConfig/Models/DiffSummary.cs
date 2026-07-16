namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

/// <summary>Summary of changes in a diff.</summary>
public class DiffSummary
{
    public int RoutesAdded { get; set; }
    public int RoutesRemoved { get; set; }
    public int RoutesModified { get; set; }
    public int ClustersAdded { get; set; }
    public int ClustersRemoved { get; set; }
    public int ClustersModified { get; set; }
    public int DestinationsAdded { get; set; }
    public int DestinationsRemoved { get; set; }
    public int DestinationsModified { get; set; }
    public int TotalChanges => RoutesAdded + RoutesRemoved + RoutesModified
                             + ClustersAdded + ClustersRemoved + ClustersModified
                             + DestinationsAdded + DestinationsRemoved + DestinationsModified;
}
