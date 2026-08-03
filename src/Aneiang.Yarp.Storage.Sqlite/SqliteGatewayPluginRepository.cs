using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>SQLite implementation of durable gateway plugin installation state.</summary>
public sealed class SqliteGatewayPluginRepository : IGatewayPluginRepository
{
    private readonly SqliteConnectionFactory _connections;

    public SqliteGatewayPluginRepository(SqliteConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<IReadOnlyList<GatewayPluginEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<GatewayPluginEntity>();
        await using var conn = await _connections.CreateConnectionAsync(cancellationToken);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT plugin_id, version, enabled, is_built_in, source_path, registration_status, last_error, installed_at, updated_at FROM gateway_plugins ORDER BY plugin_id";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(Map(reader));
        return result;
    }

    public async Task<GatewayPluginEntity?> GetAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        await using var conn = await _connections.CreateConnectionAsync(cancellationToken);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT plugin_id, version, enabled, is_built_in, source_path, registration_status, last_error, installed_at, updated_at FROM gateway_plugins WHERE plugin_id = @pluginId";
        cmd.Parameters.AddWithValue("@pluginId", pluginId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task UpsertAsync(GatewayPluginEntity plugin, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plugin.PluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(plugin.Version);
        var now = DateTime.UtcNow;
        if (plugin.InstalledAt == default)
            plugin.InstalledAt = now;
        plugin.UpdatedAt = now;

        await using var conn = await _connections.CreateConnectionAsync(cancellationToken);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO gateway_plugins (plugin_id, version, enabled, is_built_in, source_path, registration_status, last_error, installed_at, updated_at)
            VALUES (@pluginId, @version, @enabled, @isBuiltIn, @sourcePath, @status, @lastError, @installedAt, @updatedAt)
            ON CONFLICT(plugin_id) DO UPDATE SET
                version = excluded.version,
                enabled = excluded.enabled,
                is_built_in = excluded.is_built_in,
                source_path = excluded.source_path,
                registration_status = excluded.registration_status,
                last_error = excluded.last_error,
                updated_at = excluded.updated_at;
            """;
        cmd.Parameters.AddWithValue("@pluginId", plugin.PluginId);
        cmd.Parameters.AddWithValue("@version", plugin.Version);
        cmd.Parameters.AddWithValue("@enabled", plugin.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@isBuiltIn", plugin.IsBuiltIn ? 1 : 0);
        cmd.Parameters.AddWithValue("@sourcePath", (object?)plugin.SourcePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", plugin.RegistrationStatus);
        cmd.Parameters.AddWithValue("@lastError", (object?)plugin.LastError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@installedAt", plugin.InstalledAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt", plugin.UpdatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        await using var conn = await _connections.CreateConnectionAsync(cancellationToken);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM gateway_plugins WHERE plugin_id = @pluginId";
        cmd.Parameters.AddWithValue("@pluginId", pluginId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static GatewayPluginEntity Map(SqliteDataReader reader) => new()
    {
        PluginId = reader.GetString(0),
        Version = reader.GetString(1),
        Enabled = reader.GetInt32(2) != 0,
        IsBuiltIn = reader.GetInt32(3) != 0,
        SourcePath = reader.IsDBNull(4) ? null : reader.GetString(4),
        RegistrationStatus = reader.GetString(5),
        LastError = reader.IsDBNull(6) ? null : reader.GetString(6),
        InstalledAt = DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedAt = DateTime.Parse(reader.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };
}
