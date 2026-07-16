using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Exceptions;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.Waf.Services;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Application;

/// <summary>
/// Application service for configuration history, export/import, diff, snapshot, and WAF settings.
/// Encapsulates business logic previously embedded in <see cref="Controllers.ConfigHistoryController"/>.
/// </summary>
public interface IConfigHistoryAppService
{
    Task<object> ExportConfigAsync();
    Task<(bool Success, string? Message, object? Data)> ImportConfigAsync(JsonElement config, string clientIp);
    Task<object> GetConfigHistoryAsync(int page, int pageSize, string? q, string? changeType);
    Task ClearHistoryAsync();
    Task<(bool Success, string Message)> RollbackAsync(string versionId, string clientIp);
    Task<object> CreateSnapshotAsync(string? description, string clientIp);
    object GetSnapshotMetrics();
    Task<object> ConfigDiffAsync(string versionId);
    object ValidateConfig(JsonElement config);
    object GetWafSettings();
    Task<(bool Success, string? Error)> UpdateWafSettingsAsync(WafSettingsData request);
}

/// <inheritdoc/>
public class ConfigHistoryAppService : IConfigHistoryAppService
{
    private readonly IConfigPersistenceService _persistenceService;
    private readonly IConfigDiffService _diffService;
    private readonly IDynamicYarpConfigService _dynamicConfig;
    private readonly IConfigSnapshotScheduler _snapshotScheduler;
    private readonly IOptionsMonitor<ConfigHistoryOptions> _configHistoryOptions;
    private readonly IWafSettingsPersistenceService? _wafPersistence;

    public ConfigHistoryAppService(
        IConfigPersistenceService persistenceService,
        IConfigDiffService diffService,
        IDynamicYarpConfigService dynamicConfig,
        IConfigSnapshotScheduler snapshotScheduler,
        IOptionsMonitor<ConfigHistoryOptions> configHistoryOptions,
        IWafSettingsPersistenceService? wafPersistence = null)
    {
        _persistenceService = persistenceService;
        _diffService = diffService;
        _dynamicConfig = dynamicConfig;
        _snapshotScheduler = snapshotScheduler;
        _configHistoryOptions = configHistoryOptions;
        _wafPersistence = wafPersistence;
    }

    public async Task<object> ExportConfigAsync()
    {
        var config = await _persistenceService.ExportFullConfigAsync();
        return new { config, format = "yarp-standard", exportedAt = DateTime.Now };
    }

    public async Task<(bool Success, string? Message, object? Data)> ImportConfigAsync(JsonElement config, string clientIp)
    {
        var validationResult = _persistenceService.ValidateConfig(config);
        if (!validationResult.Valid)
        {
            return (false, "Invalid configuration format", new { errors = validationResult.Errors });
        }

        var importResult = await _persistenceService.ImportFullConfigAsync(config, clientIp);
        return (importResult.Success, importResult.Message, importResult);
    }

    public async Task<object> GetConfigHistoryAsync(int page, int pageSize, string? q, string? changeType)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var history = (await _persistenceService.GetHistoryAsync())
            .OrderByDescending(s => s.Timestamp)
            .ToList();

        var latestVersionId = history.FirstOrDefault()?.VersionId;
        var allItems = history.Select(s =>
        {
            var routeCount = _diffService.ParseSnapshotRoutes(s.Config).Count;
            var clusterCount = _diffService.ParseSnapshotClusters(s.Config).Count;
            var resolvedChangeType = _diffService.ResolveHistoryChangeType(s.Description);
            return new
            {
                s.VersionId,
                s.Timestamp,
                s.Description,
                s.ClientIp,
                RouteCount = routeCount,
                ClusterCount = clusterCount,
                TotalItems = routeCount + clusterCount,
                ConfigSize = s.Config.ValueKind == JsonValueKind.Undefined ? 0 : s.Config.GetRawText().Length,
                IsLatest = string.Equals(s.VersionId, latestVersionId, StringComparison.OrdinalIgnoreCase),
                ChangeType = resolvedChangeType
            };
        });

        if (!string.IsNullOrWhiteSpace(q))
        {
            allItems = allItems.Where(s =>
                (s.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                s.VersionId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (s.ClientIp?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.IsNullOrWhiteSpace(changeType))
        {
            allItems = allItems.Where(s => string.Equals(s.ChangeType, changeType, StringComparison.OrdinalIgnoreCase));
        }

        var total = allItems.Count();
        var result = allItems
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new
        {
            items = result,
            count = result.Count,
            pagination = new
            {
                page,
                pageSize,
                total,
                totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
            }
        };
    }

    public async Task ClearHistoryAsync()
    {
        await _persistenceService.ClearHistoryAsync();
    }

    public async Task<(bool Success, string Message)> RollbackAsync(string versionId, string clientIp)
    {
        var success = await _persistenceService.RollbackAsync(versionId, clientIp);
        return (success, success ? $"Rolled back to version: {versionId}" : $"Version not found: {versionId}");
    }

    public async Task<object> CreateSnapshotAsync(string? description, string clientIp)
    {
        var desc = description ?? "Manual snapshot";
        var snapshot = await _persistenceService.SaveSnapshotAsync(desc, clientIp);
        return new { snapshot.VersionId, snapshot.Description, snapshot.Timestamp };
    }

    public object GetSnapshotMetrics()
    {
        var metrics = _snapshotScheduler.GetMetrics();
        var options = _configHistoryOptions.CurrentValue;
        return new
        {
            metrics.QueueLength,
            metrics.EnqueuedCount,
            metrics.ProcessedCount,
            metrics.FailedCount,
            metrics.DroppedCount,
            options.AutoSnapshotBeforeMutation,
            options.AsyncSnapshotForLowRiskMutation,
            options.MaxSnapshots,
            options.SnapshotQueueCapacity
        };
    }

    public async Task<object> ConfigDiffAsync(string versionId)
    {
        var history = await _persistenceService.GetHistoryAsync();
        var historySnapshot = history
            .FirstOrDefault(s => string.Equals(s.VersionId, versionId, StringComparison.OrdinalIgnoreCase));

        if (historySnapshot == null)
            throw new NotFoundException("Version", versionId);

        var snapshotRoutes = _diffService.ParseSnapshotRoutes(historySnapshot.Config);
        var snapshotClusters = _diffService.ParseSnapshotClusters(historySnapshot.Config);

        var currentRoutes = _dynamicConfig.GetRoutes();
        var currentClusters = _dynamicConfig.GetClusters();

        var routeDiffs = _diffService.BuildRouteDiffs(snapshotRoutes, currentRoutes);
        var clusterDiffs = _diffService.BuildClusterDiffs(snapshotClusters, currentClusters);

        var summary = new
        {
            versionId,
            description = historySnapshot.Description,
            timestamp = historySnapshot.Timestamp,
            routesChanged = routeDiffs.Count,
            clustersChanged = clusterDiffs.Count,
            totalChanges = routeDiffs.Count + clusterDiffs.Count
        };

        return new { summary, routes = routeDiffs, clusters = clusterDiffs };
    }

    public object ValidateConfig(JsonElement config)
    {
        var result = _persistenceService.ValidateConfig(config);
        return new { valid = result.Valid, errors = result.Errors };
    }

    public object GetWafSettings()
    {
        return _wafPersistence?.Load() ?? new WafSettingsData
        {
            EnableIpCheck = true,
            EnableRequestSizeValidation = true,
            MaxRequestBodySize = 10 * 1024 * 1024,
            MaxHeaderCount = 64,
            MaxHeaderSize = 8192,
            EnableSqlInjectionDetection = true,
            EnableXssDetection = true,
            EnablePathTraversalDetection = true
        };
    }

    public async Task<(bool Success, string? Error)> UpdateWafSettingsAsync(WafSettingsData request)
    {
        if (_wafPersistence == null)
            throw new ServerException("WAF persistence service not available");

        var success = await _wafPersistence.SaveAsync(request);
        return (success, success ? null : "Failed to save WAF settings");
    }
}
