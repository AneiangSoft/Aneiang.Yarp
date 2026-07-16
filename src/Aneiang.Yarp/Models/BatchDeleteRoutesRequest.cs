namespace Aneiang.Yarp.Models;

public class BatchDeleteRoutesRequest
{
    public List<string> RouteNames { get; set; } = new();
    public string? ClientIp { get; set; }
    public bool RemoveOrphanedClusters { get; set; } = true;
}
