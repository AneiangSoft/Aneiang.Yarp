using System.Text.Json;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Interface for configuration snapshot management: export/import, version history, rollback, and validation.
/// Snapshots are persisted via <see cref="Aneiang.Yarp.Storage.IConfigHistoryRepository"/>.
/// </summary>
public interface IConfigPersistenceService
{
    /// <summary>Export full configuration in standard YARP format.</summary>
    Task<JsonElement> ExportFullConfigAsync();

    /// <summary>Import full configuration from standard YARP format.</summary>
    Task<bool> ImportFullConfigAsync(JsonElement config, string? clientIp = null);

    /// <summary>Save a configuration snapshot for version management.</summary>
    Task<ConfigSnapshot> SaveSnapshotAsync(string? description = null, string? clientIp = null);

    /// <summary>Get configuration history (async).</summary>
    Task<IReadOnlyList<ConfigSnapshot>> GetHistoryAsync();

    /// <summary>Get configuration history (synchronous wrapper for compatibility).</summary>
    IReadOnlyList<ConfigSnapshot> GetHistory();

    /// <summary>Rollback to a specific version.</summary>
    Task<bool> RollbackAsync(string versionId, string? clientIp = null);

    /// <summary>Clear all configuration history snapshots.</summary>
    Task ClearHistoryAsync();

    /// <summary>Validate configuration against basic YARP structure requirements.</summary>
    ValidationResult ValidateConfig(JsonElement config);
}
