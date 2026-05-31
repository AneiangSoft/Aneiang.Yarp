using System.Text.Json;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Services.Implements;

/// <summary>
/// Service for persisting and loading dynamic gateway configurations.
/// Delegates storage to <see cref="IDataStore"/> for pluggable backends.
/// </summary>
public class DynamicConfigPersistenceService : IDynamicConfigPersistenceService
{
    private const string Category = "dynamic-config";
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IDataStore _store;
    private readonly ILogger<DynamicConfigPersistenceService> _logger;

    public DynamicConfigPersistenceService(IDataStore store, ILogger<DynamicConfigPersistenceService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public GatewayDynamicConfig LoadConfig()
    {
        try
        {
            var config = _store.GetDocumentAsync<GatewayDynamicConfig>(Category).GetAwaiter().GetResult();
            if (config == null)
            {
                _logger.LogDebug("Dynamic config not found in store");
                return new GatewayDynamicConfig();
            }

            _logger.LogInformation(
                "Loaded dynamic config: {RouteCount} routes, {ClusterCount} clusters",
                config.Routes.Count, config.Clusters.Count);

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dynamic config from store");
            return new GatewayDynamicConfig();
        }
    }

    /// <inheritdoc />
    public void SaveConfig(GatewayDynamicConfig config)
    {
        try
        {
            config.LastModified = DateTime.Now;
            _store.SetDocumentAsync(Category, config).GetAwaiter().GetResult();

            _logger.LogInformation(
                "Saved dynamic config: {RouteCount} routes, {ClusterCount} clusters",
                config.Routes.Count, config.Clusters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save dynamic config");
            throw;
        }
    }

    /// <inheritdoc />
    public void DeleteConfig()
    {
        try
        {
            _store.DeleteDocumentAsync(Category).GetAwaiter().GetResult();
            _logger.LogInformation("Deleted dynamic config from store");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete dynamic config");
            throw;
        }
    }

    /// <inheritdoc />
    public bool ConfigFileExists()
    {
        return _store.DocumentExistsAsync(Category).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async Task<GatewayDynamicConfig> LoadConfigAsync()
    {
        try
        {
            var config = await _store.GetDocumentAsync<GatewayDynamicConfig>(Category);
            if (config == null)
            {
                _logger.LogDebug("Dynamic config not found in store");
                return new GatewayDynamicConfig();
            }

            _logger.LogInformation(
                "Loaded dynamic config: {RouteCount} routes, {ClusterCount} clusters",
                config.Routes.Count, config.Clusters.Count);

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dynamic config from store");
            return new GatewayDynamicConfig();
        }
    }

    /// <inheritdoc />
    public async Task SaveConfigAsync(GatewayDynamicConfig config)
    {
        try
        {
            config.LastModified = DateTime.Now;
            await _store.SetDocumentAsync(Category, config);

            _logger.LogInformation(
                "Saved dynamic config: {RouteCount} routes, {ClusterCount} clusters",
                config.Routes.Count, config.Clusters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save dynamic config");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteConfigAsync()
    {
        try
        {
            await _store.DeleteDocumentAsync(Category);
            _logger.LogInformation("Deleted dynamic config from store");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete dynamic config");
            throw;
        }
    }
}
