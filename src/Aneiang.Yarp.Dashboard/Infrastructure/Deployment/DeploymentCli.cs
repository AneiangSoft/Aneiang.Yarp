using Microsoft.AspNetCore.Builder;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Deployment;

/// <summary>
/// Command-line helpers for deployment configuration.
/// Allows users to pass <c>--deployment split --proxy-url ... --dashboard-url ...</c>
/// as a shortcut instead of editing appsettings.json.
/// </summary>
public static class DeploymentCli
{
    /// <summary>
    /// Parse deployment-related CLI arguments and inject them into the configuration.
    /// Supported flags: <c>--deployment</c>, <c>--proxy-url</c>, <c>--dashboard-url</c>, <c>--admin-url</c>, <c>--health-url</c>.
    /// </summary>
    public static WebApplicationBuilder ConfigureDeploymentCli(this WebApplicationBuilder builder)
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length == 0) return builder;

        var parsed = ParseArgs(args);

        if (parsed.TryGetValue("deployment", out var mode))
            builder.Configuration["Gateway:Deployment:Mode"] = mode;
        if (parsed.TryGetValue("proxy-url", out var proxyUrl))
            builder.Configuration["Kestrel:Endpoints:Proxy:Url"] = proxyUrl;
        if (parsed.TryGetValue("dashboard-url", out var dashUrl))
            builder.Configuration["Kestrel:Endpoints:Dashboard:Url"] = dashUrl;
        if (parsed.TryGetValue("admin-url", out var adminUrl))
            builder.Configuration["Kestrel:Endpoints:Admin:Url"] = adminUrl;
        if (parsed.TryGetValue("health-url", out var healthUrl))
            builder.Configuration["Kestrel:Endpoints:Health:Url"] = healthUrl;

        return builder;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string? key = args[i] switch
            {
                "--deployment"    => "deployment",
                "--proxy-url"     => "proxy-url",
                "--dashboard-url" => "dashboard-url",
                "--admin-url"     => "admin-url",
                "--health-url"    => "health-url",
                _ => null
            };
            if (key != null && i + 1 < args.Length)
            {
                result[key] = args[++i];
            }
        }
        return result;
    }
}
