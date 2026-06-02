using System.Text.Json;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Services.Implements;

/// <summary>
/// Service for persisting and loading dynamic gateway configurations.
/// Delegates storage to <see cref="IDataStore"/> for pluggable backends.
/// Uses in-memory caching to avoid blocking async calls.
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
    private GatewayDynamicConfig? _cachedConfig;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private bool _initialized;

    public DynamicConfigPersistenceService(IDataStore store, ILogger<DynamicConfigPersistenceService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Pre-loads config into memory cache during startup to avoid sync-over-async.
    /// </summary>
    public async Task PreloadAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            _cachedConfig = await LoadConfigInternalAsync(ct);
            _initialized = true;
            _logger.LogInformation(
                "Preloaded dynamic config: {RouteCount} routes, {ClusterCount} clusters",
                _cachedConfig.Routes.Count, _cachedConfig.Clusters.Count);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<GatewayDynamicConfig> LoadConfigInternalAsync(CancellationToken ct)
    {
        try
        {
            var config = await _store.GetDocumentAsync<GatewayDynamicConfig>(Category, ct);
            if (config == null)
            {
                _logger.LogDebug("Dynamic config not found in store");
                return new GatewayDynamicConfig();
            }
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dynamic config from store");
            return new GatewayDynamicConfig();
        }
    }

    /// <inheritdoc />
    public GatewayDynamicConfig LoadConfig()
    {
        if (_initialized && _cachedConfig != null)
            return _cachedConfig;

        // Fallback: only happens before preload completes (shouldn't occur in normal flow)
        try
        {
            var config = _store.GetDocumentAsync<GatewayDynamicConfig>(Category).GetAwaiter().GetResult();
            if (config == null)
            {
                _logger.LogDebug("Dynamic config not found in store");
                return new GatewayDynamicConfig();
            }
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
            _cachedConfig = config;
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
            _cachedConfig = new GatewayDynamicConfig();
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
        try
        {
            return _store.DocumentExistsAsync(Category).GetAwaiter().GetResult();
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<GatewayDynamicConfig> LoadConfigAsync()
    {
        if (_initialized && _cachedConfig != null)
            return _cachedConfig;
        return await LoadConfigInternalAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public async Task SaveConfigAsync(GatewayDynamicConfig config)
    {
        try
        {
            config.LastModified = DateTime.Now;
            await _store.SetDocumentAsync(Category, config);
            _cachedConfig = config;
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
            _cachedConfig = new GatewayDynamicConfig();
            _logger.LogInformation("Deleted dynamic config from store");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete dynamic config");
            throw;
        }
    }
}
