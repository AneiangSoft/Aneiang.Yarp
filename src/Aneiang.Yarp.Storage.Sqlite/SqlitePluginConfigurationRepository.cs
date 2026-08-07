using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>SQLite storage for versioned plugin schemas and route/cluster bindings.</summary>
public sealed class SqlitePluginConfigurationRepository : IPluginConfigurationRepository
{
    private readonly SqliteConnectionFactory _connections;

    public SqlitePluginConfigurationRepository(SqliteConnectionFactory connections)
        => _connections = connections;

    private const string BindingColumns = "id, plugin_id, plugin_version, scope, scope_id, route_uid, cluster_uid, enabled, config_json, schema_version, config_version, sort_order, created_at, updated_at";

    public async Task<IReadOnlyList<PluginBindingEntity>> GetBindingsAsync(CancellationToken ct = default)
    {
        await using var conn = await _connections.CreateConnectionAsync(ct);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {BindingColumns} FROM plugin_bindings ORDER BY scope, scope_id, sort_order, plugin_id";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<PluginBindingEntity>();
        while (await reader.ReadAsync(ct)) result.Add(ReadBinding(reader));
        return result;
    }

    public async Task<IReadOnlyList<PluginBindingEntity>> GetBindingsAsync(PluginBindingScope scope, string scopeId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        await using var conn = await _connections.CreateConnectionAsync(ct);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        var uidColumn = scope == PluginBindingScope.Route ? "route_uid" : "cluster_uid";
        cmd.CommandText = $"SELECT {BindingColumns} FROM plugin_bindings WHERE scope = @scope AND ({uidColumn} = @scopeId OR scope_id = @scopeId) ORDER BY sort_order, plugin_id";
        cmd.Parameters.AddWithValue("@scope", (int)scope);
        cmd.Parameters.AddWithValue("@scopeId", scopeId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<PluginBindingEntity>();
        while (await reader.ReadAsync(ct)) result.Add(ReadBinding(reader));
        return result;
    }

    public async Task<PluginBindingEntity?> GetBindingAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await using var conn = await _connections.CreateConnectionAsync(ct);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {BindingColumns} FROM plugin_bindings WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBinding(reader) : null;
    }

    public async Task UpsertBindingAsync(PluginBindingEntity binding, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.PluginId);
        if (!Enum.IsDefined(binding.Scope)) throw new ArgumentOutOfRangeException(nameof(binding.Scope));
        var stableUid = binding.Scope == PluginBindingScope.Route ? binding.RouteUid : binding.ClusterUid;
        if (string.IsNullOrWhiteSpace(stableUid))
            stableUid = binding.ScopeId;
        ArgumentException.ThrowIfNullOrWhiteSpace(stableUid);
        binding.ScopeId = string.IsNullOrWhiteSpace(binding.ScopeId) ? stableUid : binding.ScopeId;
        binding.RouteUid = binding.Scope == PluginBindingScope.Route ? stableUid : null;
        binding.ClusterUid = binding.Scope == PluginBindingScope.Cluster ? stableUid : null;
        if (binding.SchemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(binding.SchemaVersion));
        if (binding.ConfigVersion < 1) throw new ArgumentOutOfRangeException(nameof(binding.ConfigVersion));

        binding.UpdatedAt = DateTime.UtcNow;
        await using var conn = await _connections.CreateConnectionAsync(ct);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO plugin_bindings (id, plugin_id, plugin_version, scope, scope_id, route_uid, cluster_uid, enabled, config_json, schema_version, config_version, sort_order, created_at, updated_at)
            VALUES (@id, @pluginId, @pluginVersion, @scope, @scopeId, @routeUid, @clusterUid, @enabled, @configJson, @schemaVersion, @configVersion, @sortOrder, @createdAt, @updatedAt)
            ON CONFLICT(id) DO UPDATE SET plugin_id = excluded.plugin_id, plugin_version = excluded.plugin_version, scope = excluded.scope,
                scope_id = excluded.scope_id, route_uid = excluded.route_uid, cluster_uid = excluded.cluster_uid,
                enabled = excluded.enabled, config_json = excluded.config_json,
                schema_version = excluded.schema_version, config_version = excluded.config_version,
                sort_order = excluded.sort_order, updated_at = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("@id", binding.Id);
        cmd.Parameters.AddWithValue("@pluginId", binding.PluginId);
        cmd.Parameters.AddWithValue("@pluginVersion", binding.PluginVersion);
        cmd.Parameters.AddWithValue("@scope", (int)binding.Scope);
        cmd.Parameters.AddWithValue("@scopeId", binding.ScopeId);
        cmd.Parameters.AddWithValue("@routeUid", (object?)binding.RouteUid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@clusterUid", (object?)binding.ClusterUid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@enabled", binding.Enabled);
        cmd.Parameters.AddWithValue("@configJson", binding.ConfigJson);
        cmd.Parameters.AddWithValue("@schemaVersion", binding.SchemaVersion);
        cmd.Parameters.AddWithValue("@configVersion", binding.ConfigVersion);
        cmd.Parameters.AddWithValue("@sortOrder", binding.Order);
        cmd.Parameters.AddWithValue("@createdAt", binding.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt", binding.UpdatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> DeleteBindingAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await using var conn = await _connections.CreateConnectionAsync(ct);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM plugin_bindings WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<PluginSchemaEntity>> GetSchemasAsync(CancellationToken ct = default)
    {
        await using var conn = await _connections.CreateConnectionAsync(ct);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT plugin_id, schema_version, schema_json, created_at, updated_at FROM plugin_schemas ORDER BY plugin_id, schema_version";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<PluginSchemaEntity>();
        while (await reader.ReadAsync(ct)) result.Add(ReadSchema(reader));
        return result;
    }

    public async Task<PluginSchemaEntity?> GetSchemaAsync(string pluginId, int schemaVersion, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        await using var conn = await _connections.CreateConnectionAsync(ct);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT plugin_id, schema_version, schema_json, created_at, updated_at FROM plugin_schemas WHERE plugin_id = @pluginId AND schema_version = @schemaVersion";
        cmd.Parameters.AddWithValue("@pluginId", pluginId);
        cmd.Parameters.AddWithValue("@schemaVersion", schemaVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadSchema(reader) : null;
    }

    public async Task UpsertSchemaAsync(PluginSchemaEntity schema, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema.PluginId);
        if (schema.SchemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(schema.SchemaVersion));
        schema.UpdatedAt = DateTime.UtcNow;
        await using var conn = await _connections.CreateConnectionAsync(ct);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO plugin_schemas (plugin_id, schema_version, schema_json, created_at, updated_at)
            VALUES (@pluginId, @schemaVersion, @schemaJson, @createdAt, @updatedAt)
            ON CONFLICT(plugin_id, schema_version) DO UPDATE SET schema_json = excluded.schema_json, updated_at = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("@pluginId", schema.PluginId);
        cmd.Parameters.AddWithValue("@schemaVersion", schema.SchemaVersion);
        cmd.Parameters.AddWithValue("@schemaJson", schema.SchemaJson);
        cmd.Parameters.AddWithValue("@createdAt", schema.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt", schema.UpdatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static PluginBindingEntity ReadBinding(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0), PluginId = reader.GetString(1), PluginVersion = reader.GetString(2),
        Scope = (PluginBindingScope)reader.GetInt32(3), ScopeId = reader.GetString(4),
        RouteUid = reader.IsDBNull(5) ? null : reader.GetString(5),
        ClusterUid = reader.IsDBNull(6) ? null : reader.GetString(6),
        Enabled = reader.GetBoolean(7), ConfigJson = reader.GetString(8),
        SchemaVersion = reader.GetInt32(9), ConfigVersion = reader.GetInt64(10), Order = reader.GetInt32(11),
        CreatedAt = DateTime.Parse(reader.GetString(12), null, System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedAt = DateTime.Parse(reader.GetString(13), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };

    // --- Strategy Presets ---

    private const string PresetColumns = "id, name, description, plugin_id, config_json, schema_version, created_at, updated_at";

    public async Task<IReadOnlyList<PluginConfigPresetEntity>> GetPresetsAsync(CancellationToken ct = default)
    {
        await using var conn = await _connections.CreateConnectionAsync(ct);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {PresetColumns} FROM plugin_config_presets ORDER BY plugin_id, name";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<PluginConfigPresetEntity>();
        while (await reader.ReadAsync(ct)) result.Add(ReadPreset(reader));
        return result;
    }

    public async Task<IReadOnlyList<PluginConfigPresetEntity>> GetPresetsByPluginAsync(string pluginId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        await using var conn = await _connections.CreateConnectionAsync(ct);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {PresetColumns} FROM plugin_config_presets WHERE plugin_id = @pluginId ORDER BY name";
        cmd.Parameters.AddWithValue("@pluginId", pluginId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<PluginConfigPresetEntity>();
        while (await reader.ReadAsync(ct)) result.Add(ReadPreset(reader));
        return result;
    }

    public async Task<PluginConfigPresetEntity?> GetPresetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await using var conn = await _connections.CreateConnectionAsync(ct);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {PresetColumns} FROM plugin_config_presets WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadPreset(reader) : null;
    }

    public async Task UpsertPresetAsync(PluginConfigPresetEntity preset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentException.ThrowIfNullOrWhiteSpace(preset.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(preset.PluginId);
        preset.UpdatedAt = DateTime.UtcNow;
        await using var conn = await _connections.CreateConnectionAsync(ct);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO plugin_config_presets (id, name, description, plugin_id, config_json, schema_version, created_at, updated_at)
            VALUES (@id, @name, @description, @pluginId, @configJson, @schemaVersion, @createdAt, @updatedAt)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, description = excluded.description,
                plugin_id = excluded.plugin_id, config_json = excluded.config_json,
                schema_version = excluded.schema_version, updated_at = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("@id", preset.Id);
        cmd.Parameters.AddWithValue("@name", preset.Name);
        cmd.Parameters.AddWithValue("@description", (object?)preset.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pluginId", preset.PluginId);
        cmd.Parameters.AddWithValue("@configJson", preset.ConfigJson);
        cmd.Parameters.AddWithValue("@schemaVersion", preset.SchemaVersion);
        cmd.Parameters.AddWithValue("@createdAt", preset.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt", preset.UpdatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> DeletePresetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await using var conn = await _connections.CreateConnectionAsync(ct);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM plugin_config_presets WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static PluginConfigPresetEntity ReadPreset(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        PluginId = reader.GetString(3),
        ConfigJson = reader.GetString(4),
        SchemaVersion = reader.GetInt32(5),
        CreatedAt = DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedAt = DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };

    private static PluginSchemaEntity ReadSchema(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        PluginId = reader.GetString(0), SchemaVersion = reader.GetInt32(1), SchemaJson = reader.GetString(2),
        CreatedAt = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedAt = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };
}
