using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Alert.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Models;

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
