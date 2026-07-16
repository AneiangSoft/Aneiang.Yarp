namespace Aneiang.Yarp.Dashboard.Infrastructure.Deployment;

/// <summary>
/// Deployment / startup mode controlling how Dashboard and Proxy share or split ports.
/// </summary>
public enum DeploymentMode
{
    /// <summary>Auto-detect based on the number of configured Kestrel endpoints.</summary>
    Auto,

    /// <summary>Force single-port mode: Dashboard UI and Proxy forward share the same port.</summary>
    AllInOne,

    /// <summary>Force single-process multi-port mode with per-endpoint role routing.</summary>
    Split,

    /// <summary>Disable Dashboard UI/API; only YARP proxy forwarding is active.</summary>
    ProxyOnly,

    /// <summary>Disable YARP proxy forwarding; only Dashboard UI/API is active.</summary>
    DashboardOnly
}
