using Aneiang.Yarp.Models;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Interface for persisting and loading dynamic gateway configurations.
/// </summary>
public interface IDynamicConfigPersistenceService
{
    /// <summary>Load dynamic configuration from file.</summary>
    GatewayDynamicConfig LoadConfig();

    /// <summary>Save dynamic configuration to file using atomic write.</summary>
    void SaveConfig(GatewayDynamicConfig config);

    /// <summary>Delete the dynamic configuration file.</summary>
    void DeleteConfig();

    /// <summary>Check if dynamic configuration file exists.</summary>
    bool ConfigFileExists();

    /// <summary>Load dynamic configuration from file asynchronously.</summary>
    Task<GatewayDynamicConfig> LoadConfigAsync();

    /// <summary>Save dynamic configuration to file asynchronously using atomic write.</summary>
    Task SaveConfigAsync(GatewayDynamicConfig config);

    /// <summary>Delete the dynamic configuration file asynchronously.</summary>
    Task DeleteConfigAsync();
}
