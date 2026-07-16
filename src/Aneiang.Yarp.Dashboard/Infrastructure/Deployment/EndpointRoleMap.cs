namespace Aneiang.Yarp.Dashboard.Infrastructure.Deployment;

/// <summary>
/// Resolved mapping from a Kestrel endpoint to its deployment role.
/// Populated at startup by <see cref="EndpointRoleResolver"/>.
/// </summary>
public class EndpointRoleMap
{
    /// <summary>Kestrel endpoint name from configuration (e.g. "Proxy").</summary>
    public string EndpointName { get; init; } = string.Empty;

    /// <summary>Listening port.</summary>
    public int Port { get; init; }

    /// <summary>Bound IP address (e.g. "0.0.0.0" or "127.0.0.1").</summary>
    public string IpAddress { get; init; } = string.Empty;

    /// <summary>Role: Proxy / Dashboard / Admin / Health / All.</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>True when bound to a publicly routable IP (0.0.0.0, *, ::, empty).</summary>
    public bool IsPubliclyBound { get; init; }
}
