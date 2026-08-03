using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Abstractions.Storage.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>
/// SQLite implementation of <see cref="IPluginRuntimeStateRepository"/>.
/// Uses INSERT OR REPLACE for upsert semantics on the composite primary key.
/// </summary>
public sealed class SqlitePluginRuntimeStateRepository : IPluginRuntimeStateRepository
{
    private readonly SqliteConnectionFactory _factory;
    private readonly ILogger<SqlitePluginRuntimeStateRepository> _logger;

    public SqlitePluginRuntimeStateRepository(
        SqliteConnectionFactory factory,
        ILogger<SqlitePluginRuntimeStateRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PluginRuntimeStateEntity?> GetAsync(
        string pluginId, string targetType, string targetUid, string nodeId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = _factory.CreateConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT plugin_id, target_type, target_uid, node_id, state_json, updated_at
            FROM plugin_runtime_states
            WHERE plugin_id = @pluginId AND target_type = @targetType
              AND target_uid = @targetUid AND node_id = @nodeId
            """;
        cmd.Parameters.AddWithValue("@pluginId", pluginId);
        cmd.Parameters.AddWithValue("@targetType", targetType);
        cmd.Parameters.AddWithValue("@targetUid", targetUid);
        cmd.Parameters.AddWithValue("@nodeId", nodeId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return MapEntity(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PluginRuntimeStateEntity>> GetAllByPluginAsync(
        string pluginId, CancellationToken cancellationToken = default)
    {
        await using var conn = _factory.CreateConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT plugin_id, target_type, target_uid, node_id, state_json, updated_at
            FROM plugin_runtime_states
            WHERE plugin_id = @pluginId
            """;
        cmd.Parameters.AddWithValue("@pluginId", pluginId);

        var results = new List<PluginRuntimeStateEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(MapEntity(reader));

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PluginRuntimeStateEntity>> GetByTargetAsync(
        string pluginId, string targetType, string targetUid,
        CancellationToken cancellationToken = default)
    {
        await using var conn = _factory.CreateConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT plugin_id, target_type, target_uid, node_id, state_json, updated_at
            FROM plugin_runtime_states
            WHERE plugin_id = @pluginId AND target_type = @targetType AND target_uid = @targetUid
            """;
        cmd.Parameters.AddWithValue("@pluginId", pluginId);
        cmd.Parameters.AddWithValue("@targetType", targetType);
        cmd.Parameters.AddWithValue("@targetUid", targetUid);

        var results = new List<PluginRuntimeStateEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(MapEntity(reader));

        return results;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(PluginRuntimeStateEntity entity, CancellationToken cancellationToken = default)
    {
        await using var conn = _factory.CreateConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO plugin_runtime_states (plugin_id, target_type, target_uid, node_id, state_json, updated_at)
            VALUES (@pluginId, @targetType, @targetUid, @nodeId, @stateJson, @updatedAt)
            ON CONFLICT(plugin_id, target_type, target_uid, node_id)
            DO UPDATE SET state_json = @stateJson, updated_at = @updatedAt
            """;
        cmd.Parameters.AddWithValue("@pluginId", entity.PluginId);
        cmd.Parameters.AddWithValue("@targetType", entity.TargetType);
        cmd.Parameters.AddWithValue("@targetUid", entity.TargetUid);
        cmd.Parameters.AddWithValue("@nodeId", entity.NodeId);
        cmd.Parameters.AddWithValue("@stateJson", entity.StateJson);
        cmd.Parameters.AddWithValue("@updatedAt", entity.UpdatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string pluginId, string targetType, string targetUid, string nodeId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = _factory.CreateConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM plugin_runtime_states
            WHERE plugin_id = @pluginId AND target_type = @targetType
              AND target_uid = @targetUid AND node_id = @nodeId
            """;
        cmd.Parameters.AddWithValue("@pluginId", pluginId);
        cmd.Parameters.AddWithValue("@targetType", targetType);
        cmd.Parameters.AddWithValue("@targetUid", targetUid);
        cmd.Parameters.AddWithValue("@nodeId", nodeId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    /// <inheritdoc />
    public async Task<int> DeleteAllByPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        await using var conn = _factory.CreateConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM plugin_runtime_states WHERE plugin_id = @pluginId";
        cmd.Parameters.AddWithValue("@pluginId", pluginId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static PluginRuntimeStateEntity MapEntity(SqliteDataReader reader)
    {
        return new PluginRuntimeStateEntity
        {
            PluginId = reader.GetString("plugin_id"),
            TargetType = reader.GetString("target_type"),
            TargetUid = reader.GetString("target_uid"),
            NodeId = reader.GetString("node_id"),
            StateJson = reader.GetString("state_json"),
            UpdatedAt = DateTime.TryParse(reader.GetString("updated_at"), out var dt) ? dt : DateTime.UtcNow
        };
    }
}
