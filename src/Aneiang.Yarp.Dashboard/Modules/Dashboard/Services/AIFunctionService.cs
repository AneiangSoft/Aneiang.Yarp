using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;

/// <summary>
/// Defines and executes Function Calling tools for the AI assistant.
/// Read tools execute automatically; write tools return a confirmation request to the frontend.
/// </summary>
public class AIFunctionService
{
    private readonly IServiceProvider _services;
    private readonly IOptions<AIOptions> _options;

    private static readonly HashSet<string> WriteTools = new()
    {
        "toggle_route", "create_route", "delete_route", "delete_cluster",
        "reset_circuit_breaker", "update_waf_config", "toggle_plugin"
    };

    public AIFunctionService(IServiceProvider services, IOptions<AIOptions> options)
    {
        _services = services;
        _options = options;
    }

    public bool IsWriteTool(string toolName) => WriteTools.Contains(toolName);

    public static List<object> GetToolDefinitions() =>
    [
        MakeTool("list_routes", "List all YARP routes with RouteId, cluster, match path, and enabled status."),
        MakeTool("list_clusters", "List all YARP clusters with ClusterId, destinations, and load balancing policy."),
        MakeTool("get_cluster_health", "Get health status of all destinations across all clusters."),
        MakeTool("get_traffic_stats", "Get traffic statistics for a recent time window.", [
            ("minutes", "integer", "Time window in minutes (default: 60)")
        ]),
        MakeTool("list_plugins", "List all installed plugins with name, enabled status, and category."),
        MakeTool("get_config_history", "Get recent configuration change history.", [
            ("count", "integer", "Number of recent changes (default: 10)")
        ]),
        MakeTool("toggle_route", "Enable or disable a YARP route.", [
            ("routeId", "string", "RouteId to toggle", true),
            ("enabled", "boolean", "true=enable, false=disable", true)
        ]),
        MakeTool("create_route", "Create a new YARP route. If the cluster does not exist it will be created.", [
            ("routeId", "string", "Unique route ID", true),
            ("path", "string", "ASP.NET route template e.g. /api/users/{**remainder}", true),
            ("clusterId", "string", "Target cluster ID", true),
            ("destination", "string", "Destination address e.g. http://localhost:5000", false)
        ]),
        MakeTool("delete_route", "Delete a YARP route.", [
            ("routeId", "string", "RouteId to delete", true)
        ]),
        MakeTool("delete_cluster", "Delete a YARP cluster. Fails if routes reference it.", [
            ("clusterId", "string", "ClusterId to delete", true)
        ]),
    ];

    private static object MakeTool(string name, string desc, List<(string, string, string)>? props = null)
    {
        var required = props?.Where(p => p.Item1.Length > 0).Select(p => p.Item1).ToArray() ?? [];
        var properties = new Dictionary<string, object>();
        if (props != null)
        {
            foreach (var (pname, ptype, pdesc) in props)
            {
                properties[pname] = new { type = ptype, description = pdesc };
            }
        }
        return new
        {
            type = "function",
            function = new
            {
                name,
                description = desc,
                parameters = new { type = "object", properties, required }
            }
        };
    }

    // Overload with required flag
    private static object MakeTool(string name, string desc, List<(string Name, string Type, string Desc, bool Required)> props)
    {
        var required = props.Where(p => p.Required).Select(p => p.Name).ToArray();
        var properties = new Dictionary<string, object>();
        foreach (var p in props)
            properties[p.Name] = new { type = p.Type, description = p.Desc };
        return new
        {
            type = "function",
            function = new { name, description = desc, parameters = new { type = "object", properties, required } }
        };
    }

    /// <summary>Execute a read tool and return the result as a JSON string.</summary>
    public async Task<string> ExecuteReadToolAsync(string toolName, JsonElement? arguments)
    {
        try
        {
            return toolName switch
            {
                "list_routes" => await GetConfigSectionAsync("Routes"),
                "list_clusters" => await GetConfigSectionAsync("Clusters"),
                "get_cluster_health" => await GetConfigSectionAsync("Clusters"),
                "get_traffic_stats" => await GetTrafficStatsAsync(),
                "list_plugins" => await GetPluginsAsync(),
                "get_config_history" => await GetConfigSectionAsync("Routes"),
                _ => "{\"error\":\"Unknown read tool: " + toolName + "\"}"
            };
        }
        catch (Exception ex)
        {
            return "{\"error\":\"" + ex.Message.Replace("\"", "\\\"") + "\"}";
        }
    }

    /// <summary>Execute a write tool after user confirmation and return the result as a JSON string.</summary>
    public async Task<string> ExecuteWriteToolAsync(string toolName, JsonElement? arguments)
    {
        try
        {
            var dynamicConfig = _services.GetRequiredService<IDynamicYarpConfigService>();
            return toolName switch
            {
                "toggle_route" => await ToggleRouteAsync(dynamicConfig, arguments),
                "create_route" => await CreateRouteAsync(dynamicConfig, arguments),
                "delete_route" => await DeleteRouteAsync(dynamicConfig, arguments),
                "delete_cluster" => await DeleteClusterAsync(dynamicConfig, arguments),
                _ => "{\"error\":\"Unknown write tool: " + toolName + "\"}"
            };
        }
        catch (Exception ex)
        {
            return "{\"error\":\"" + ex.Message.Replace("\"", "\\\"") + "\"}";
        }
    }

    private static async Task<string> ToggleRouteAsync(IDynamicYarpConfigService cfg, JsonElement? args)
    {
        var routeId = GetArg(args, "routeId");
        var enabled = GetBoolArg(args, "enabled");
        if (string.IsNullOrEmpty(routeId))
            return "{\"error\":\"routeId is required\"}";
        var result = await cfg.TrySetRouteEnabled(routeId, enabled, "ai-assistant");
        return JsonSerializer.Serialize(new { routeId, enabled, success = result.Success, message = result.Message });
    }

    private static async Task<string> CreateRouteAsync(IDynamicYarpConfigService cfg, JsonElement? args)
    {
        var routeId = GetArg(args, "routeId");
        var path = GetArg(args, "path");
        var clusterId = GetArg(args, "clusterId");
        var destination = GetArg(args, "destination");
        if (string.IsNullOrEmpty(routeId) || string.IsNullOrEmpty(path))
            return "{\"error\":\"routeId and path are required\"}";

        // Ensure the target cluster exists before creating the route.
        if (string.IsNullOrEmpty(destination))
        {
            var existingCluster = cfg.GetCluster(clusterId ?? "");
            if (existingCluster == null)
                return "{\"error\":\"cluster '" + clusterId + "' does not exist. Provide a 'destination' to create it.\"}";
        }
        else
        {
            var existing = cfg.GetCluster(clusterId ?? "");
            if (existing == null)
            {
                var addCluster = await cfg.TryAddCluster(clusterId ?? routeId, new Dictionary<string, string> { ["d1"] = destination },
                    loadBalancingPolicy: null, healthCheck: null, source: "ai-assistant", createdBy: "ai-assistant");
                if (!addCluster.Success)
                    return "{\"error\":\"Failed to create cluster: " + addCluster.Message + "\"}";
            }
        }

        var request = new RegisterRouteRequest
        {
            RouteName = routeId,
            ClusterName = clusterId ?? "",
            MatchPath = path,
            DestinationAddress = destination ?? ""
        };
        var result = await cfg.TryAddRoute(request, "ai-assistant", "ai-assistant");
        return JsonSerializer.Serialize(new { routeId, path, clusterId, success = result.Success, message = result.Message });
    }

    private static async Task<string> DeleteRouteAsync(IDynamicYarpConfigService cfg, JsonElement? args)
    {
        var routeId = GetArg(args, "routeId");
        if (string.IsNullOrEmpty(routeId))
            return "{\"error\":\"routeId is required\"}";
        var result = await cfg.TryRemoveRoute(routeId);
        return JsonSerializer.Serialize(new { routeId, success = result.Success, message = result.Message });
    }

    private static async Task<string> DeleteClusterAsync(IDynamicYarpConfigService cfg, JsonElement? args)
    {
        var clusterId = GetArg(args, "clusterId");
        if (string.IsNullOrEmpty(clusterId))
            return "{\"error\":\"clusterId is required\"}";
        var result = await cfg.TryRemoveCluster(clusterId);
        return JsonSerializer.Serialize(new { clusterId, success = result.Success, message = result.Message });
    }

    private static string? GetArg(JsonElement? args, string name)
    {
        if (args == null || args.Value.ValueKind != JsonValueKind.Object) return null;
        if (args.Value.TryGetProperty(name, out var el))
            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        return null;
    }

    private static bool GetBoolArg(JsonElement? args, string name)
    {
        if (args == null || args.Value.ValueKind != JsonValueKind.Object) return false;
        if (args.Value.TryGetProperty(name, out var el))
        {
            if (el.ValueKind == JsonValueKind.True) return true;
            if (el.ValueKind == JsonValueKind.False) return false;
            return string.Equals(el.GetString(), "true", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private async Task<string> GetConfigSectionAsync(string section)
    {
        var configService = _services.GetRequiredService<ConfigPersistenceService>();
        var config = await configService.ExportFullConfigAsync();
        var json = JsonSerializer.Serialize(config);
        var node = JsonNode.Parse(json);
        var result = node?["ReverseProxy"]?[section];
        return result?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
               ?? "{\"error\":\"No " + section + " found\"}";
    }

    private async Task<string> GetTrafficStatsAsync()
    {
        var configService = _services.GetRequiredService<ConfigPersistenceService>();
        var config = await configService.ExportFullConfigAsync();
        var json = JsonSerializer.Serialize(config);
        var node = JsonNode.Parse(json);
        var routeCount = node?["ReverseProxy"]?["Routes"]?.AsObject().Count ?? 0;
        var clusterCount = node?["ReverseProxy"]?["Clusters"]?.AsObject().Count ?? 0;
        return JsonSerializer.Serialize(new
        {
            routes = routeCount,
            clusters = clusterCount,
            note = "Detailed traffic stats require the TrafficMetrics plugin."
        });
    }

    private async Task<string> GetPluginsAsync()
    {
        try
        {
            var pluginManagerType = Type.GetType("Aneiang.Yarp.Plugin.GatewayPluginManager, Aneiang.Yarp.Plugin");
            var pluginManager = pluginManagerType is not null ? _services.GetService(pluginManagerType) : null;
            if (pluginManager != null)
            {
                var prop = pluginManager.GetType().GetProperty("Manifests") ?? pluginManager.GetType().GetProperty("Plugins");
                if (prop != null)
                {
                    var plugins = prop.GetValue(pluginManager);
                    return JsonSerializer.Serialize(plugins);
                }
            }
        }
        catch { }
        return "{\"note\":\"Plugin list available on the Plugin Resources page.\"}";
    }

    /// <summary>Build the system prompt with current gateway context.</summary>
    public async Task<string> BuildSystemPromptAsync()
    {
        var configService = _services.GetRequiredService<ConfigPersistenceService>();
        var config = await configService.ExportFullConfigAsync();
        var json = JsonSerializer.Serialize(config);
        var node = JsonNode.Parse(json);
        var routes = node?["ReverseProxy"]?["Routes"]?.AsObject();
        var clusters = node?["ReverseProxy"]?["Clusters"]?.AsObject();

        var routeList = routes?.Select(kvp => $"{kvp.Key} (path: {kvp.Value?["Match"]?["Path"]}, cluster: {kvp.Value?["ClusterId"]})").ToList() ?? [];
        var clusterList = clusters?.Select(kvp =>
        {
            var dests = kvp.Value?["Destinations"]?.AsObject();
            return $"{kvp.Key} ({dests?.Count ?? 0} destinations)";
        }).ToList() ?? [];

        var sb = new StringBuilder();
        sb.AppendLine("You are an AI assistant for the Aneiang.Yarp gateway dashboard.");
        sb.AppendLine("You help users manage routes, clusters, plugins, and monitor gateway health.");
        sb.AppendLine("Read tools (list_*, get_*) execute automatically. Write tools (toggle_*, create_*, delete_*) are executed only after the user confirms a popup dialog, so call them directly whenever the user requests a change.");
        sb.AppendLine("Do NOT ask the user to type a confirmation message - the frontend will show confirm/cancel buttons automatically.");
        sb.AppendLine("Respond in the same language as the user's message.");
        sb.AppendLine();
        sb.AppendLine("Current gateway context:");
        sb.AppendLine($"- Routes ({routeList.Count}): {string.Join(", ", routeList.Take(20))}");
        sb.AppendLine($"- Clusters ({clusterList.Count}): {string.Join(", ", clusterList.Take(20))}");
        sb.AppendLine();
        sb.AppendLine("When asked about routes, clusters, or health, use the appropriate tools.");
        sb.AppendLine("When the user requests a change (enable/disable/create/delete route or cluster), call the corresponding write tool immediately.");

        return sb.ToString();
    }
}
