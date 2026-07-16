using System.Reflection;
using Aneiang.Yarp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aneiang.Yarp.Services;

internal static class RegistrationOptionsResolver
{
    public static bool IsEnabled(GatewayRegistrationOptions options)
        => options.Enabled ?? !string.IsNullOrWhiteSpace(options.GatewayUrl);

    public static string GetRouteName(GatewayRegistrationOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.RouteName)
            ? options.RouteName : Assembly.GetEntryAssembly()?.GetName().Name ?? "my-service";
    }

    public static string GetClusterName(GatewayRegistrationOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.ClusterName)
            ? options.ClusterName
            : (!string.IsNullOrWhiteSpace(options.RouteName) ? options.RouteName : Assembly.GetEntryAssembly()?.GetName().Name ?? "my-service");
    }

    public static string GetMatchPath(GatewayRegistrationOptions options)
    {
        var path = !string.IsNullOrWhiteSpace(options.MatchPath) ? options.MatchPath : "/{**catch-all}";
        if (!path.StartsWith("/")) path = "/" + path;
        return path;
    }

    public static int GetOrder(GatewayRegistrationOptions options) => options.Order ?? int.MaxValue;

    public static bool GetAutoResolveIp(GatewayRegistrationOptions options) => options.AutoResolveIp ?? true;

    public static int GetTimeoutSeconds(GatewayRegistrationOptions options) => options.TimeoutSeconds ?? 5;

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

    public static string? GetDestinationAddress(GatewayRegistrationOptions options, IServiceProvider sp)
    {
        if (!string.IsNullOrWhiteSpace(options.DestinationAddress)) return options.DestinationAddress;

        var config = sp.GetRequiredService<IConfiguration>();
        var endpoints = new List<string>();

        // Collect all available endpoints
        // 1. From "Kestrel:EndPoints" (explicit config, highest priority)
        var kestrelSection = config.GetSection("Kestrel:EndPoints");
        if (kestrelSection.Exists())
        {
            foreach (var ep in kestrelSection.GetChildren())
            {
                var url = ep["Url"]?.Trim();
                if (!string.IsNullOrEmpty(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    endpoints.Add(url);
            }
        }

        // 2. From "Urls" / "urls" config (may come from launchSettings.json)
        var urls = config["urls"] ?? config["Urls"];
        if (!string.IsNullOrWhiteSpace(urls))
        {
            foreach (var u in urls.Split(';'))
            {
                var trimmed = u.Trim();
                if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    endpoints.Add(trimmed);
            }
        }

        // 3. From ASPNETCORE_URLS environment variable
        var envUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (!string.IsNullOrWhiteSpace(envUrls))
        {
            foreach (var u in envUrls.Split(';'))
            {
                var trimmed = u.Trim();
                if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    endpoints.Add(trimmed);
            }
        }

        // Determine preferred scheme based on gateway URL protocol
        var gwUrl = options.GatewayUrl;
        var preferHttps = !string.IsNullOrWhiteSpace(gwUrl) &&
                          gwUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        // Select matching endpoint by protocol
        var preferred = preferHttps
            ? endpoints.FirstOrDefault(e => e.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            : endpoints.FirstOrDefault(e => e.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                                           && !e.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        if (preferred != null) return preferred;

        // Fallback: use first available endpoint
        if (endpoints.Count > 0) return endpoints[0];

        // Default fallback
        var env = sp.GetRequiredService<IHostEnvironment>();
        var port = env.IsDevelopment() ? "5001" : "5000";
        return preferHttps ? $"https://localhost:{port}" : $"http://localhost:{port}";
    }

    public static bool UseIpIsolation(GatewayRegistrationOptions options) => options.UseIpIsolation ?? false;
}
