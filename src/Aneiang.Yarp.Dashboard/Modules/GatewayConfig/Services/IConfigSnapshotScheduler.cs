using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

public interface IConfigSnapshotScheduler
{
    bool QueueSnapshot(string description, string? clientIp = null);
    ConfigSnapshotMetrics GetMetrics();
}
