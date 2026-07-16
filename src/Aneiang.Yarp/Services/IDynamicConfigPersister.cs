using Aneiang.Yarp.Models;

namespace Aneiang.Yarp.Services;

internal interface IDynamicConfigPersister
{
    Task<GatewayDynamicConfig> LoadAsync();

    Task SaveAsync(GatewayDynamicConfig config, string operationName, string? targetName = null);
}
