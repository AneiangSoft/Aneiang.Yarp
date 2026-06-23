using Microsoft.Extensions.Configuration;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Deployment;

/// <summary>
/// Resolves Kestrel endpoint configuration to deployment role mappings.
/// Walks <c>Kestrel:Endpoints</c>, parses each URL, and assigns a role
/// (Proxy / Dashboard / Admin / Health / All) to each port.
/// Also normalizes the deployment mode: explicit value wins, otherwise Auto picks based on endpoint count.
/// </summary>
public class EndpointRoleResolver
{
    private readonly Dictionary<int, EndpointRoleMap> _byPort = new();

    /// <summary>Build the role map and normalize the deployment mode based on configured endpoints.</summary>
    public EndpointRoleResolver(IConfiguration config, DeploymentOptions options)
    {
        var kestrelSection = config.GetSection("Kestrel:Endpoints");
        var endpointCount = kestrelSection.Exists() ? kestrelSection.GetChildren().Count() : 1;
        var resolvedMode = ResolveMode(options.Mode, endpointCount);

        foreach (var endpoint in kestrelSection.GetChildren())
        {
            var url = endpoint["Url"];
            if (string.IsNullOrEmpty(url)) continue;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;

            var role = ResolveRole(endpoint.Key, options.EndpointRoles, resolvedMode);
            var map = new EndpointRoleMap
            {
                EndpointName = endpoint.Key,
                Port = uri.Port,
                IpAddress = uri.Host,
                Role = role,
                IsPubliclyBound = IsPublicBind(uri.Host)
            };
            _byPort[uri.Port] = map;
            options.ResolvedEndpoints.Add(map);
        }

        options.Mode = resolvedMode;
    }

    /// <summary>Look up the role map by port. Returns null when the port is not registered.</summary>
    public EndpointRoleMap? GetByPort(int port) =>
        _byPort.TryGetValue(port, out var map) ? map : null;

    /// <summary>Enumerate all resolved endpoint maps.</summary>
    public IEnumerable<EndpointRoleMap> GetAll() => _byPort.Values;

    private static DeploymentMode ResolveMode(DeploymentMode configured, int count) =>
        configured != DeploymentMode.Auto
            ? configured
            : (count > 1 ? DeploymentMode.Split : DeploymentMode.AllInOne);

    private static string ResolveRole(string endpointName, Dictionary<string, string> roles, DeploymentMode mode)
    {
        if (roles.TryGetValue(endpointName, out var r) && !string.IsNullOrEmpty(r))
            return r;

        var lower = endpointName.ToLowerInvariant();
        if (lower.Contains("proxy")) return "Proxy";
        if (lower.Contains("dashboard")) return "Dashboard";
        if (lower.Contains("admin")) return "Admin";
        if (lower.Contains("health")) return "Health";

        return mode == DeploymentMode.AllInOne ? "All" : "Proxy";
    }

    private static bool IsPublicBind(string host) =>
        host == "0.0.0.0" || host == "*" || host == "::" || string.IsNullOrEmpty(host);
}
