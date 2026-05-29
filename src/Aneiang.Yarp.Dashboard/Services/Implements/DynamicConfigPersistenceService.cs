using System.Text.Json;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Services.Implements;

/// <summary>
/// Service for persisting and loading dynamic gateway configurations.
/// Thread-safe: callers are responsible for external synchronization.
/// Uses atomic file writes (write-to-temp + rename) to prevent corruption.
/// </summary>
public class DynamicConfigPersistenceService : IDynamicConfigPersistenceService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _configFilePath;
    private readonly ILogger<DynamicConfigPersistenceService> _logger;

    public DynamicConfigPersistenceService(string configFilePath, ILogger<DynamicConfigPersistenceService> logger)
    {
        _configFilePath = configFilePath;
        _logger = logger;
    }

    /// <inheritdoc />
    public GatewayDynamicConfig LoadConfig()
    {
        if (!File.Exists(_configFilePath))
        {
            _logger.LogDebug("Dynamic config file not found: {FilePath}", _configFilePath);
            return new GatewayDynamicConfig();
        }

        try
        {
            var json = File.ReadAllText(_configFilePath);
            var config = JsonSerializer.Deserialize<GatewayDynamicConfig>(json, _jsonOptions);
            
            if (config == null)
            {
                _logger.LogWarning("Dynamic config file is invalid, returning empty config");
                return new GatewayDynamicConfig();
            }

            _logger.LogInformation(
                "Loaded dynamic config: {RouteCount} routes, {ClusterCount} clusters",
                config.Routes.Count, 
                config.Clusters.Count);
            
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dynamic config from {FilePath}", _configFilePath);
            return new GatewayDynamicConfig();
        }
    }

    /// <inheritdoc />
    public void SaveConfig(GatewayDynamicConfig config)
    {
        try
        {
            config.LastModified = DateTime.UtcNow;
            
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            
            var directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tmpPath = _configFilePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _configFilePath, overwrite: true);
            
            _logger.LogInformation(
                "Saved dynamic config: {RouteCount} routes, {ClusterCount} clusters to {FilePath}",
                config.Routes.Count,
                config.Clusters.Count,
                _configFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save dynamic config to {FilePath}", _configFilePath);
            throw;
        }
    }

    /// <inheritdoc />
    public void DeleteConfig()
    {
        if (File.Exists(_configFilePath))
        {
            try
            {
                File.Delete(_configFilePath);
                _logger.LogInformation("Deleted dynamic config file: {FilePath}", _configFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete dynamic config file: {FilePath}", _configFilePath);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public bool ConfigFileExists()
    {
        return File.Exists(_configFilePath);
    }

    /// <inheritdoc />
    public async Task<GatewayDynamicConfig> LoadConfigAsync()
    {
        if (!File.Exists(_configFilePath))
        {
            _logger.LogDebug("Dynamic config file not found: {FilePath}", _configFilePath);
            return new GatewayDynamicConfig();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configFilePath);
            var config = JsonSerializer.Deserialize<GatewayDynamicConfig>(json, _jsonOptions);

            if (config == null)
            {
                _logger.LogWarning("Dynamic config file is invalid, returning empty config");
                return new GatewayDynamicConfig();
            }

            _logger.LogInformation(
                "Loaded dynamic config: {RouteCount} routes, {ClusterCount} clusters",
                config.Routes.Count,
                config.Clusters.Count);

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dynamic config from {FilePath}", _configFilePath);
            return new GatewayDynamicConfig();
        }
    }

    /// <inheritdoc />
    public async Task SaveConfigAsync(GatewayDynamicConfig config)
    {
        try
        {
            config.LastModified = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(config, _jsonOptions);

            var directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tmpPath = _configFilePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, _configFilePath, overwrite: true);

            _logger.LogInformation(
                "Saved dynamic config: {RouteCount} routes, {ClusterCount} clusters to {FilePath}",
                config.Routes.Count,
                config.Clusters.Count,
                _configFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save dynamic config to {FilePath}", _configFilePath);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteConfigAsync()
    {
        if (File.Exists(_configFilePath))
        {
            try
            {
                File.Delete(_configFilePath);
                _logger.LogInformation("Deleted dynamic config file: {FilePath}", _configFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete dynamic config file: {FilePath}", _configFilePath);
                throw;
            }
        }
        await Task.CompletedTask;
    }
}
