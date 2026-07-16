using Aneiang.Yarp.Models;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

internal interface IDynamicConfigPublisher
{
    void Publish(GatewayDynamicConfig config, long version);

    ClusterConfig SanitizeCluster(ClusterConfig cluster);
}
