using System.Text.Json;
using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Service for persisting and loading dynamic gateway configurations.
/// Stores runtime modifications to gateway-dynamic.json to survive restarts.
/// </summary>
public class DynamicConfigPersistenceService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _configFilePath;
    private readonly ILogger<DynamicConfigPersistenceService> _logger;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of DynamicConfigPersistenceService.
    /// </summary>
    /// <param name="configFilePath">Path to gateway-dynamic.json file.</param>
    /// <param name="logger">Logger instance.</param>
    public DynamicConfigPersistenceService(string configFilePath, ILogger<DynamicConfigPersistenceService> logger)
    {
        _configFilePath = configFilePath;
        _logger = logger;
    }

    /// <summary>
    /// Load dynamic configuration from file.
    /// Returns empty config if file doesn't exist.
    /// </summary>
    public GatewayDynamicConfig LoadConfig()
    {
        lock (_lock)
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
    }

    /// <summary>
    /// Save dynamic configuration to file.
    /// </summary>
    public void SaveConfig(GatewayDynamicConfig config)
    {
        lock (_lock)
        {
            try
            {
                config.LastModified = DateTime.UtcNow;
                
                var json = JsonSerializer.Serialize(config, _jsonOptions);
                
                // Ensure directory exists
                var directory = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_configFilePath, json);
                
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
    }

    /// <summary>
    /// Delete the dynamic configuration file.
    /// </summary>
    public void DeleteConfig()
    {
        lock (_lock)
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
    }

    /// <summary>
    /// Check if dynamic configuration file exists.
    /// </summary>
    public bool ConfigFileExists()
    {
        return File.Exists(_configFilePath);
    }
}
