using Aneiang.Yarp.Models;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Persistence layer for <see cref="GatewayDynamicConfig"/> — loads from and saves to
/// <see cref="Storage.IRouteRepository"/> and <see cref="Storage.IClusterRepository"/>.
/// </summary>
internal interface IDynamicConfigPersister
{
    /// <summary>Load the full dynamic config from repository.</summary>
    Task<GatewayDynamicConfig> LoadAsync();

    /// <summary>
    /// Persist the entire config to repository. Deletes stale DB rows not present in the
    /// in-memory set, then upserts routes/clusters/destinations from the config.
    /// </summary>
    Task SaveAsync(GatewayDynamicConfig config, string operationName, string? targetName = null);
}
