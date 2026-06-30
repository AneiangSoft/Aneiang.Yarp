using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Infrastructure;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Controllers;

/// <summary>
/// Base class for config management controllers providing shared dependencies
/// and helper methods (cache invalidation, snapshot scheduling, client IP resolution).
/// </summary>
public abstract class ConfigControllerBase : ControllerBase
{
    /// <summary>Configuration persistence service (export/import/snapshot/history/rollback).</summary>
    protected readonly IConfigPersistenceService PersistenceService;

    /// <summary>Dynamic YARP configuration service (live route/cluster CRUD).</summary>
    protected readonly IDynamicYarpConfigService DynamicConfig;

    /// <summary>Memory cache for dashboard query cache keys.</summary>
    protected readonly IMemoryCache MemoryCache;

    /// <summary>Async snapshot queue scheduler.</summary>
    protected readonly IConfigSnapshotScheduler SnapshotScheduler;

    /// <summary>Configuration history options monitor.</summary>
    protected readonly IOptionsMonitor<ConfigHistoryOptions> ConfigHistoryOptions;

    protected ConfigControllerBase(
        IConfigPersistenceService persistenceService,
        IDynamicYarpConfigService dynamicConfig,
        IMemoryCache memoryCache,
        IConfigSnapshotScheduler snapshotScheduler,
        IOptionsMonitor<ConfigHistoryOptions> configHistoryOptions)
    {
        PersistenceService = persistenceService;
        DynamicConfig = dynamicConfig;
        MemoryCache = memoryCache;
        SnapshotScheduler = snapshotScheduler;
        ConfigHistoryOptions = configHistoryOptions;
    }

    /// <summary>
    /// Invalidates the dashboard query caches so the UI immediately reflects
    /// the latest configuration after any mutation (save/delete/rename/rollback/import).
    /// </summary>
    protected void InvalidateQueryCaches()
    {
        MemoryCache.Remove("dashboard:routes:query");
        MemoryCache.Remove("dashboard:clusters:query");
    }

    /// <summary>
    /// Queue or execute a low-risk snapshot based on configured options.
    /// </summary>
    protected async Task SnapshotLowRiskMutationAsync(string description)
    {
        var options = ConfigHistoryOptions.CurrentValue;
        if (!options.AutoSnapshotBeforeMutation)
            return;

        var clientIp = GetClientIp();
        if (options.AsyncSnapshotForLowRiskMutation)
        {
            SnapshotScheduler.QueueSnapshot(description, clientIp);
            return;
        }

        await PersistenceService.SaveSnapshotAsync(description, clientIp);
    }

    /// <summary>
    /// Gets the client IP address from the request, considering proxy headers.
    /// </summary>
    protected string? GetClientIp()
    {
        return ClientIpResolver.GetClientIp(HttpContext);
    }

    /// <summary>
    /// Checks whether a JSON element contains a property whose value differs from the expected ID.
    /// </summary>
    protected static bool ContainsDifferentId(JsonElement config, string expectedId, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (config.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var actualId = value.GetString();
                if (!string.IsNullOrWhiteSpace(actualId) && !string.Equals(actualId, expectedId, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Ensures the cluster referenced by a route exists. If it does not and the payload carries
    /// a "destinations" object, the cluster is created from those destinations.
    /// </summary>
    protected async Task EnsureClusterForRouteAsync(string clusterId, JsonElement config)
    {
        if (DynamicConfig.GetCluster(clusterId) != null)
            return;

        if (!(config.TryGetProperty("destinations", out var destsProp) || config.TryGetProperty("Destinations", out destsProp)))
            return;
        if (destsProp.ValueKind != JsonValueKind.Object)
            return;

        var destinations = new Dictionary<string, string>();
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

        if (destinations.Count > 0)
            await DynamicConfig.TryAddCluster(clusterId, destinations, null, null, "dashboard", "dashboard-user");
    }
}
