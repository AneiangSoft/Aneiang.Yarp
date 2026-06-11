using Aneiang.Yarp.Dashboard.Modules.Policy.Models;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Services;

/// <summary>
/// Interface for persisting gateway policies to a JSON file.
/// </summary>
public interface IGatewayPolicyPersistenceService
{
    /// <summary>Load gateway policies from the persistence file.</summary>
    GatewayPolicyCollection Load();

    /// <summary>Save gateway policies to the persistence file.</summary>
    void Save(GatewayPolicyCollection collection);
}
