namespace Aneiang.Yarp.Models;

public class GatewayDynamicConfig
{
    public long Version { get; set; } = 1;

    public DateTime LastModified { get; set; } = DateTime.Now;

    public List<DynamicRouteConfig> Routes { get; set; } = new();

    public List<DynamicClusterConfig> Clusters { get; set; } = new();
}
