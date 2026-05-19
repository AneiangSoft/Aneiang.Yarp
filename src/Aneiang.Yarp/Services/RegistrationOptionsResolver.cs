using System.Reflection;
using Aneiang.Yarp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Resolves smart defaults for gateway registration options.
/// Handles route/cluster naming, path generation, transform building, and destination address resolution.
/// </summary>
internal static class RegistrationOptionsResolver
{
    /// <summary>Determines whether registration is enabled.</summary>
    public static bool IsEnabled(GatewayRegistrationOptions options)
        => options.Enabled ?? !string.IsNullOrWhiteSpace(options.GatewayUrl);

    /// <summary>Gets the resolved route name.</summary>
    public static string GetRouteName(GatewayRegistrationOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.RouteName)
            ? options.RouteName : Assembly.GetEntryAssembly()?.GetName().Name ?? "my-service";
    }

    /// <summary>Gets the resolved cluster name.</summary>
    public static string GetClusterName(GatewayRegistrationOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.ClusterName)
            ? options.ClusterName
            : (!string.IsNullOrWhiteSpace(options.RouteName) ? options.RouteName : Assembly.GetEntryAssembly()?.GetName().Name ?? "my-service");
    }

    /// <summary>Gets the resolved match path.</summary>
    public static string GetMatchPath(GatewayRegistrationOptions options)
    {
        var path = !string.IsNullOrWhiteSpace(options.MatchPath) ? options.MatchPath : "/{**catch-all}";
        if (!path.StartsWith("/")) path = "/" + path;
        return path;
    }

    /// <summary>Gets the resolved route order. Default: 50.</summary>
    public static int GetOrder(GatewayRegistrationOptions options) => options.Order ?? 50;

    /// <summary>Gets whether auto IP resolution is enabled. Default: true.</summary>
    public static bool GetAutoResolveIp(GatewayRegistrationOptions options) => options.AutoResolveIp ?? true;

    /// <summary>Gets the HTTP timeout in seconds. Default: 5.</summary>
    public static int GetTimeoutSeconds(GatewayRegistrationOptions options) => options.TimeoutSeconds ?? 5;

    /// <summary>
    /// Build transforms list based on configuration priority:
    /// 1. Custom Transforms (highest)
    /// 2. DownstreamPathPrefix
    /// </summary>
    public static List<Dictionary<string, string>>? GetTransforms(GatewayRegistrationOptions options)
    {
        // Priority 1: User-defined custom transforms
        if (options.Transforms != null && options.Transforms.Count > 0)
            return options.Transforms;

        // Priority 2: DownstreamPathPrefix override
        if (!string.IsNullOrWhiteSpace(options.DownstreamPathPrefix))
        {
            return new List<Dictionary<string, string>>
            {
                new() { { "PathSet", options.DownstreamPathPrefix + "/{**catch-all}" } }
            };
        }

        return null;
    }

    /// <summary>Resolves the destination address (auto-detects from Kestrel if not set).</summary>
    public static string? GetDestinationAddress(GatewayRegistrationOptions options, IServiceProvider sp)
    {
        if (!string.IsNullOrWhiteSpace(options.DestinationAddress)) return options.DestinationAddress;

        // Auto-detect from Kestrel urls
        var config = sp.GetRequiredService<IConfiguration>();
        var urls = config["urls"] ?? config["Urls"];
        if (!string.IsNullOrWhiteSpace(urls))
        {
            var first = urls.Split(';')[0].Trim();
            if (first.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return first;
        }

        var env = sp.GetRequiredService<IHostEnvironment>();
        var port = env.IsDevelopment() ? "5001" : "5000";
        return $"http://localhost:{port}";
    }

    /// <summary>Gets whether IP-based isolation is enabled.</summary>
    public static bool UseIpIsolation(GatewayRegistrationOptions options) => options.UseIpIsolation ?? false;
}
