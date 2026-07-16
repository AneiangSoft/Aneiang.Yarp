using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Infrastructure.Exceptions;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Controllers;

/// <summary>
/// Cluster CRUD operations - save, delete, rename.
/// Exceptions are handled globally by <see cref="Infrastructure.Filters.GlobalExceptionFilter"/>.
/// </summary>
[Route("api/config")]
[ApiController]
public class ClusterConfigController : ConfigControllerBase
{
    private readonly ILogger<ClusterConfigController> _logger;
    private readonly IGatewayIdentityService _identityService;

    public ClusterConfigController(
        IConfigPersistenceService persistenceService,
        IDynamicYarpConfigService dynamicConfig,
        ILogger<ClusterConfigController> logger,
        IMemoryCache memoryCache,
        IGatewayIdentityService identityService,
        IConfigSnapshotScheduler snapshotScheduler,
        IOptionsMonitor<ConfigHistoryOptions> configHistoryOptions)
        : base(persistenceService, dynamicConfig, memoryCache, snapshotScheduler, configHistoryOptions)
    {
        _logger = logger;
        _identityService = identityService;
    }

    /// <summary>
    /// Save or update a single cluster from a full native YARP cluster config.
    /// </summary>
    [HttpPut("clusters/{clusterId}")]
    public async Task<IActionResult> SaveCluster(string clusterId, [FromBody] JsonElement config)
    {
        _logger.LogInformation("Save cluster requested: {ClusterId}", clusterId);

        ClusterConfig? cluster;
        try
        {
            cluster = Aneiang.Yarp.Serialization.YarpJsonConfig.DeserializeCluster(config);
        }
        catch (Exception parseEx)
        {
            throw new ValidationException("Invalid cluster configuration: " + parseEx.Message);
        }

        if (cluster == null)
            throw new ValidationException("Cluster configuration is required");

        cluster = cluster with { ClusterId = clusterId };

        if (cluster.Destinations == null || cluster.Destinations.Count == 0)
            throw new ValidationException("At least one destination is required");

        SnapshotScheduler.QueueSnapshot($"After cluster '{clusterId}' saved via dashboard", GetClientIp());

        var result = await DynamicConfig.TryAddClusterConfig(cluster, "dashboard", "dashboard-user");

        if (result.Success) InvalidateQueryCaches();
        return result.Success
            ? Ok(ApiResponse.Ok(new { clusterId }, result.Message))
            : BadRequest(ApiResponse.Fail(result.Message));
    }

    /// <summary>
    /// Delete a cluster.
    /// </summary>
    [HttpDelete("clusters/{clusterId}")]
    public async Task<IActionResult> DeleteCluster(string clusterId)
    {
        _logger.LogInformation("Delete cluster requested: {ClusterId}", clusterId);

        await PersistenceService.SaveSnapshotAsync($"Before cluster '{clusterId}' deleted via dashboard", GetClientIp());

        var result = await DynamicConfig.TryRemoveCluster(clusterId);

        if (result.Success) InvalidateQueryCaches();
        return result.Success
            ? Ok(ApiResponse.Ok(new { clusterId }, result.Message))
            : BadRequest(ApiResponse.Fail(result.Message));
    }

    /// <summary>
    /// Rename a cluster. Updates all referencing routes atomically.
    /// </summary>
    [HttpPut("clusters/{clusterId}/rename")]
    public async Task<IActionResult> RenameCluster(string clusterId, [FromBody] JsonElement config)
    {
        _logger.LogInformation("Rename cluster requested: {OldClusterId}", clusterId);

        string? newClusterId = null;
        if (config.TryGetProperty("newClusterId", out var newIdProp))
            newClusterId = newIdProp.GetString();
        else if (config.TryGetProperty("clusterId", out var newIdProp2))
            newClusterId = newIdProp2.GetString();

        if (string.IsNullOrWhiteSpace(newClusterId))
            throw new ValidationException("newClusterId is required");

        Dictionary<string, string> destinations = new();
        if (config.TryGetProperty("destinations", out var destsProp) || config.TryGetProperty("Destinations", out destsProp))
        {
            if (destsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var dest in destsProp.EnumerateObject())
                {
                    var address = dest.Value.ValueKind == JsonValueKind.String
                        ? dest.Value.GetString() ?? string.Empty
                        : dest.Value.TryGetProperty("address", out var addrProp)
                            ? addrProp.GetString() ?? string.Empty
                            : dest.Value.TryGetProperty("Address", out var addrPascalProp)
                                ? addrPascalProp.GetString() ?? string.Empty
                                : string.Empty;
                    if (!string.IsNullOrEmpty(address))
                        destinations[dest.Name] = address;
                }
            }
        }

        if (destinations.Count == 0)
            throw new ValidationException("destinations is required");

        string? loadBalancingPolicy = null;
        if (config.TryGetProperty("loadBalancingPolicy", out var lbpProp))
            loadBalancingPolicy = lbpProp.GetString();

        var result = await _identityService.RenameClusterAsync(
            clusterId, newClusterId, destinations, loadBalancingPolicy,
            clientIp: GetClientIp(), operatorName: "dashboard-user",
            ct: HttpContext.RequestAborted);

        if (result.Success) InvalidateQueryCaches();
        return result.Success
            ? Ok(ApiResponse.Ok(new { oldClusterId = clusterId, newClusterId }, result.Message))
            : BadRequest(ApiResponse.Fail(result.Message));
    }
}
