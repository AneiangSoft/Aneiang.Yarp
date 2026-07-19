using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Application;

/// <summary>
/// Loads configuration templates from embedded JSON resources and applies them.
/// </summary>
public class ConfigTemplateService : IConfigTemplateService
{
    private const string ResourcePrefix = "Aneiang.Yarp.Dashboard.Infrastructure.Templates.";
    private const string ResourceSuffix = ".json";

    private readonly IDynamicYarpConfigService _dynamicConfig;
    private readonly ILogger<ConfigTemplateService> _logger;
    private List<ConfigTemplate>? _cache;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ConfigTemplateService(IDynamicYarpConfigService dynamicConfig, ILogger<ConfigTemplateService> logger)
    {
        _dynamicConfig = dynamicConfig;
        _logger = logger;
    }

    public Task<List<ConfigTemplate>> GetAllAsync(CancellationToken ct = default)
    {
        EnsureLoaded();
        return Task.FromResult(_cache ?? []);
    }

    public Task<ConfigTemplate?> GetAsync(string templateId, CancellationToken ct = default)
    {
        EnsureLoaded();
        return Task.FromResult(_cache?.FirstOrDefault(t => t.Id == templateId));
    }

    public async Task<ApplyResult> ApplyAsync(string templateId, Dictionary<string, string> variables, CancellationToken ct = default)
    {
        EnsureLoaded();
        var template = _cache?.FirstOrDefault(t => t.Id == templateId);
        if (template == null)
            return new ApplyResult { Success = false, Message = $"Template '{templateId}' not found." };

        try
        {
            var configJson = template.Config.GetRawText();

            // Replace variables in config JSON
            foreach (var kvp in variables)
            {
                configJson = configJson.Replace("{{" + kvp.Key + "}}", kvp.Value, StringComparison.OrdinalIgnoreCase);
            }

            var config = JsonDocument.Parse(configJson);
            var routesCreated = 0;
            var clustersCreated = 0;

            // Parse and apply routes
            if (config.RootElement.TryGetProperty("Routes", out var routesElement))
            {
                foreach (var routeProp in routesElement.EnumerateObject())
                {
                    var routeName = routeProp.Name;
                    var routeConfig = routeProp.Value;

                    var route = JsonSerializer.Deserialize<RouteConfig>(routeConfig.GetRawText(), JsonOpts);
                    if (route != null)
                    {
                        var routeWithId = route with { RouteId = routeName };
                        await _dynamicConfig.TryAddRouteConfig(routeWithId, "template", "template-apply");
                        routesCreated++;
                    }
                }
            }

            // Parse and apply clusters
            if (config.RootElement.TryGetProperty("Clusters", out var clustersElement))
            {
                foreach (var clusterProp in clustersElement.EnumerateObject())
                {
                    var clusterId = clusterProp.Name;
                    var clusterConfig = clusterProp.Value;

                    var cluster = JsonSerializer.Deserialize<ClusterConfig>(clusterConfig.GetRawText(), JsonOpts);
                    if (cluster != null)
                    {
                        var clusterWithId = cluster with { ClusterId = clusterId };
                        await _dynamicConfig.TryAddClusterConfig(clusterWithId, "template", "template-apply");
                        clustersCreated++;
                    }
                }
            }

            await _dynamicConfig.SaveDynamicConfig();

            _logger.LogInformation("[Templates] Applied template {TemplateId}: {Routes} routes, {Clusters} clusters",
                templateId, routesCreated, clustersCreated);

            return new ApplyResult
            {
                Success = true,
                Message = $"Template '{template.Name}' applied successfully.",
                ImportedRoutes = routesCreated,
                ImportedClusters = clustersCreated
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Templates] Failed to apply template {TemplateId}", templateId);
            return new ApplyResult { Success = false, Message = ex.Message };
        }
    }

    private void EnsureLoaded()
    {
        if (_cache != null) return;

        lock (_lock)
        {
            if (_cache != null) return;

            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames()
                    .Where(n => n.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase) &&
                                n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                _cache = [];

                foreach (var resourceName in resourceNames)
                {
                    try
                    {
                        using var stream = assembly.GetManifestResourceStream(resourceName);
                        if (stream == null) continue;

                        using var reader = new StreamReader(stream);
                        var json = reader.ReadToEnd();
                        var template = JsonSerializer.Deserialize<ConfigTemplate>(json, JsonOpts);

                        if (template != null && !string.IsNullOrEmpty(template.Id))
                            _cache.Add(template);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Templates] Failed to load resource: {Resource}", resourceName);
                    }
                }

                _logger.LogInformation("[Templates] Loaded {Count} templates", _cache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Templates] Failed to load templates");
                _cache = [];
            }
        }
    }
}
