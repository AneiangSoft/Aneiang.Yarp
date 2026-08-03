using System.Threading;
using System.Threading.Tasks;
using Aneiang.Yarp.Storage.Abstractions.Storage.Entities;

namespace Aneiang.Yarp.Storage;

/// <summary>
/// Persists plugin runtime state (circuit breaker, rate-limit stats, WAF counts, etc.)
/// separately from configuration data.
/// </summary>
public interface IPluginRuntimeStateRepository
{
    /// <summary>Get a specific runtime state entry.</summary>
    Task<PluginRuntimeStateEntity?> GetAsync(
        string pluginId, string targetType, string targetUid, string nodeId,
        CancellationToken cancellationToken = default);

    /// <summary>Get all runtime state entries for a plugin.</summary>
    Task<IReadOnlyList<PluginRuntimeStateEntity>> GetAllByPluginAsync(
        string pluginId, CancellationToken cancellationToken = default);

    /// <summary>Get all runtime state entries for a plugin and target.</summary>
    Task<IReadOnlyList<PluginRuntimeStateEntity>> GetByTargetAsync(
        string pluginId, string targetType, string targetUid,
        CancellationToken cancellationToken = default);

    /// <summary>Insert or update a runtime state entry (upsert by composite key).</summary>
    Task UpsertAsync(PluginRuntimeStateEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Delete a specific runtime state entry.</summary>
    Task<bool> DeleteAsync(
        string pluginId, string targetType, string targetUid, string nodeId,
        CancellationToken cancellationToken = default);

    /// <summary>Delete all runtime state entries for a plugin.</summary>
    Task<int> DeleteAllByPluginAsync(string pluginId, CancellationToken cancellationToken = default);
}
