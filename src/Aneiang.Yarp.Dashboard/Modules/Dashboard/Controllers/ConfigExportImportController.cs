using System.Text.Json;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RouteConfig = global::Yarp.ReverseProxy.Configuration.RouteConfig;
using ClusterConfig = global::Yarp.ReverseProxy.Configuration.ClusterConfig;
using DestinationConfig = global::Yarp.ReverseProxy.Configuration.DestinationConfig;
using HealthCheckConfig = global::Yarp.ReverseProxy.Configuration.HealthCheckConfig;
using ForwarderRequestConfig = global::Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig;
using HttpClientConfig = global::Yarp.ReverseProxy.Configuration.HttpClientConfig;
using SessionAffinityConfig = global::Yarp.ReverseProxy.Configuration.SessionAffinityConfig;
using RouteMatch = global::Yarp.ReverseProxy.Configuration.RouteMatch;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Configuration export and import endpoints.
/// </summary>
[ApiController]
[Route("api/config")]
public sealed class ConfigExportImportController : ControllerBase
{
    private readonly IDynamicYarpConfigService _dynamicConfig;
    private readonly IPluginConfigurationRepository _pluginConfigRepository;
    private readonly IGatewaySnapshotCompiler _snapshotCompiler;
    private readonly IGatewaySnapshotPublisher _snapshotPublisher;
    private readonly ILogger<ConfigExportImportController> _logger;

    public ConfigExportImportController(
        IDynamicYarpConfigService dynamicConfig,
        IPluginConfigurationRepository pluginConfigRepository,
        IGatewaySnapshotCompiler snapshotCompiler,
        IGatewaySnapshotPublisher snapshotPublisher,
        ILogger<ConfigExportImportController> logger)
    {
        _dynamicConfig = dynamicConfig;
        _pluginConfigRepository = pluginConfigRepository;
        _snapshotCompiler = snapshotCompiler;
        _snapshotPublisher = snapshotPublisher;
        _logger = logger;
    }

    /// <summary>Export current configuration (clusters, routes, plugin bindings) as JSON.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var routes = _dynamicConfig.GetRoutes();
        var clusters = _dynamicConfig.GetClusters();
        var bindings = await _pluginConfigRepository.GetBindingsAsync(ct);

        var export = new
        {
            version = "1.0",
            exportedAt = DateTimeOffset.UtcNow,
            clusters = clusters.Select(c => new
            {
                clusterId = c.ClusterId,
                loadBalancingPolicy = c.LoadBalancingPolicy,
                healthCheck = c.HealthCheck,
                httpRequest = c.HttpRequest,
                httpClient = c.HttpClient,
                sessionAffinity = c.SessionAffinity,
                destinations = c.Destinations?.Select(d => new
                {
                    address = d.Value.Address,
                    health = d.Value.Health,
                    metadata = d.Value.Metadata
                }).ToDictionary(d => d.address, d => d),
                metadata = c.Metadata
            }),
            routes = routes.Select(r => new
            {
                routeId = r.RouteId,
                clusterId = r.ClusterId,
                order = r.Order,
                match = r.Match,
                transforms = r.Transforms,
                authorizationPolicy = r.AuthorizationPolicy,
                corsPolicy = r.CorsPolicy,
                rateLimiterPolicy = r.RateLimiterPolicy,
                timeoutPolicy = r.TimeoutPolicy,
                metadata = r.Metadata
            }),
            pluginBindings = bindings.Select(b => new
            {
                id = b.Id,
                pluginId = b.PluginId,
                scope = b.Scope,
                scopeId = b.ScopeId,
                enabled = b.Enabled,
                configJson = b.ConfigJson,
                schemaVersion = b.SchemaVersion,
                order = b.Order,
                configVersion = b.ConfigVersion
            })
        };

        return Ok(new { code = 200, data = export });
    }

    /// <summary>Import configuration from JSON. Replaces all routes and clusters, updates plugin bindings.</summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] JsonElement body, CancellationToken ct)
    {
        try
        {
            if (!body.TryGetProperty("data", out var data))
                return BadRequest(new { code = 400, message = "Missing 'data' field." });

            var routeCount = 0;
            var clusterCount = 0;
            var bindingCount = 0;

            // Parse and apply clusters + routes via ReplaceAllConfig
            var newRoutes = new List<RouteConfig>();
            var newClusters = new List<ClusterConfig>();

            if (data.TryGetProperty("routes", out var routesEl))
            {
                foreach (var r in routesEl.EnumerateArray())
                {
                    var routeId = r.TryGetProperty("routeId", out var rid) ? rid.GetString() : null;
                    var clusterId = r.TryGetProperty("clusterId", out var cid) ? cid.GetString() : null;
                    if (string.IsNullOrEmpty(routeId)) continue;

                    var match = r.TryGetProperty("match", out var m) ? m.Deserialize<RouteMatch>() : new RouteMatch();
                    var order = r.TryGetProperty("order", out var o) ? (int?)o.GetInt32() : null;
                    var transforms = r.TryGetProperty("transforms", out var t) ? t.Deserialize<IReadOnlyList<IReadOnlyDictionary<string, string>>>() : null;
                    var authPolicy = r.TryGetProperty("authorizationPolicy", out var ap) ? ap.GetString() : null;
                    var corsPolicy = r.TryGetProperty("corsPolicy", out var cp) ? cp.GetString() : null;
                    var rateLimiterPolicy = r.TryGetProperty("rateLimiterPolicy", out var rl) ? rl.GetString() : null;
                    var timeout = r.TryGetProperty("timeoutPolicy", out var tp) ? tp.GetString() : null;
                    var metadata = r.TryGetProperty("metadata", out var md) ? md.Deserialize<IReadOnlyDictionary<string, string>>() : null;

                    newRoutes.Add(new RouteConfig
                    {
                        RouteId = routeId!,
                        ClusterId = clusterId,
                        Match = match ?? new RouteMatch(),
                        Order = order,
                        Transforms = transforms,
                        AuthorizationPolicy = authPolicy,
                        CorsPolicy = corsPolicy,
                        RateLimiterPolicy = rateLimiterPolicy,
                        TimeoutPolicy = timeout,
                        Metadata = metadata
                    });
                    routeCount++;
                }
            }

            if (data.TryGetProperty("clusters", out var clustersEl))
            {
                foreach (var c in clustersEl.EnumerateArray())
                {
                    var clusterId = c.TryGetProperty("clusterId", out var cid) ? cid.GetString() : null;
                    if (string.IsNullOrEmpty(clusterId)) continue;

                    var lbPolicy = c.TryGetProperty("loadBalancingPolicy", out var lb) ? lb.GetString() : null;
                    var healthCheck = c.TryGetProperty("healthCheck", out var hc) ? hc.Deserialize<HealthCheckConfig>() : null;
                    var httpRequest = c.TryGetProperty("httpRequest", out var hr) ? hr.Deserialize<ForwarderRequestConfig>() : null;
                    var httpClient = c.TryGetProperty("httpClient", out var hcl) ? hcl.Deserialize<HttpClientConfig>() : null;
                    var sessionAffinity = c.TryGetProperty("sessionAffinity", out var sa) ? sa.Deserialize<SessionAffinityConfig>() : null;
                    var metadata = c.TryGetProperty("metadata", out var md) ? md.Deserialize<IReadOnlyDictionary<string, string>>() : null;

                    var destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase);
                    if (c.TryGetProperty("destinations", out var dests))
                    {
                        foreach (var d in dests.EnumerateObject())
                        {
                            var address = d.Value.TryGetProperty("address", out var addr) ? addr.GetString() : null;
                            if (string.IsNullOrEmpty(address)) continue;
                            var health = d.Value.TryGetProperty("health", out var h) ? h.GetString() : null;
                            var destMeta = d.Value.TryGetProperty("metadata", out var dm) ? dm.Deserialize<IReadOnlyDictionary<string, string>>() : null;
                            destinations[d.Name] = new DestinationConfig { Address = address!, Health = health, Metadata = destMeta };
                        }
                    }

                    newClusters.Add(new ClusterConfig
                    {
                        ClusterId = clusterId!,
                        LoadBalancingPolicy = lbPolicy,
                        HealthCheck = healthCheck,
                        HttpRequest = httpRequest,
                        HttpClient = httpClient,
                        SessionAffinity = sessionAffinity,
                        Destinations = destinations,
                        Metadata = metadata
                    });
                    clusterCount++;
                }
            }

            // Apply routes and clusters
            await _dynamicConfig.ReplaceAllConfig(newRoutes, newClusters, "import", "dashboard-user");
            await _dynamicConfig.SaveDynamicConfig();

            // Apply plugin bindings
            if (data.TryGetProperty("pluginBindings", out var bindingsEl))
            {
                var existingBindings = await _pluginConfigRepository.GetBindingsAsync(ct);
                var existingIds = existingBindings.Select(b => b.Id).ToHashSet();

                foreach (var b in bindingsEl.EnumerateArray())
                {
                    var id = b.TryGetProperty("id", out var bid) ? bid.GetString() : null;
                    var pluginId = b.TryGetProperty("pluginId", out var pid) ? pid.GetString() : null;
                    var scope = b.TryGetProperty("scope", out var sc) ? sc.GetString() : null;
                    var scopeId = b.TryGetProperty("scopeId", out var sid) ? sid.GetString() : null;
                    if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(scopeId)) continue;

                    var entity = new Storage.Entities.PluginBindingEntity
                    {
                        Id = id ?? Guid.NewGuid().ToString("N"),
                        PluginId = pluginId,
                        Scope = Enum.TryParse<PluginBindingScope>(scope, true, out var scopeVal) ? scopeVal : (scope == "Cluster" ? PluginBindingScope.Cluster : PluginBindingScope.Route),
                        ScopeId = scopeId,
                        Enabled = b.TryGetProperty("enabled", out var en) ? en.GetBoolean() : true,
                        ConfigJson = b.TryGetProperty("configJson", out var cj) ? cj.GetString() : "{}",
                        SchemaVersion = b.TryGetProperty("schemaVersion", out var sv) ? sv.GetInt32() : 1,
                        Order = b.TryGetProperty("order", out var ord) ? ord.GetInt32() : 0,
                        ConfigVersion = b.TryGetProperty("configVersion", out var cv) ? cv.GetInt32() : 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    await _pluginConfigRepository.UpsertBindingAsync(entity, ct);
                    bindingCount++;
                }
            }

            // Publish snapshot
            var allBindings = await _pluginConfigRepository.GetBindingsAsync(ct);
            var snapshot = await _snapshotCompiler.CompileAsync(newRoutes, newClusters, _snapshotPublisher.Current.Version + 1, ct, allBindings);
            _snapshotPublisher.Publish(snapshot);

            _logger.LogInformation("Config import completed: {Routes} routes, {Clusters} clusters, {Bindings} bindings", routeCount, clusterCount, bindingCount);

            return Ok(new
            {
                code = 200,
                message = "Import completed",
                imported = new { routes = routeCount, clusters = clusterCount, bindings = bindingCount }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Config import failed");
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }
}
