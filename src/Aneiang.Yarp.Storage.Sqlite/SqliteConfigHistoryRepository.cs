using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Aneiang.Yarp.Storage.Sqlite;

public sealed class SqliteConfigHistoryRepository : IConfigHistoryRepository
{
    private readonly SqliteConnectionFactory _connections;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteConfigHistoryRepository(SqliteConnectionFactory connections) => _connections = connections;

    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            await InitializeAsync(ct);
        }
        finally { _initLock.Release(); }
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        await SqliteRepositoryInitializer.EnsureTableExistsAsync(_connections, "yarp_config_history", ct);
        _initialized = true;
    }

    public async Task<ConfigHistoryEntity?> GetConfigHistoryAsync(string versionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_config_history WHERE version_id = @id";
        cmd.Parameters.AddWithValue("@id", versionId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<ConfigHistoryEntity>> GetConfigHistoryListAsync(int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM yarp_config_history ORDER BY created_at DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        var list = new List<ConfigHistoryEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(Map(reader));
        return list.AsReadOnly();
    }

    public async Task SaveConfigHistoryAsync(ConfigHistoryEntity history, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO yarp_config_history (version_id, description, client_ip, config_data, diff_data, created_by, created_at)
            VALUES (@id, @desc, @ip, @cfg, @diff, @cb, @ca)
            ON CONFLICT(version_id) DO UPDATE SET
                description = @desc, client_ip = @ip, config_data = @cfg, diff_data = @diff
            """;
        cmd.Parameters.AddWithValue("@id", history.VersionId);
        cmd.Parameters.AddWithValue("@desc", history.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ip", history.ClientIp ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cfg", history.ConfigData);
        cmd.Parameters.AddWithValue("@diff", history.DiffData ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cb", history.CreatedBy);
        cmd.Parameters.AddWithValue("@ca", history.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteConfigHistoryAsync(string versionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM yarp_config_history WHERE version_id = @id";
        cmd.Parameters.AddWithValue("@id", versionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteOldConfigHistoryAsync(int keepCount, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM yarp_config_history
            WHERE version_id NOT IN (SELECT version_id FROM yarp_config_history ORDER BY created_at DESC LIMIT @keep)
            """;
        cmd.Parameters.AddWithValue("@keep", keepCount);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ClearConfigHistoryAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM yarp_config_history";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ConfigHistoryEntity Map(SqliteDataReader r) => new()
    {
        VersionId = r.GetString(0),
        Description = r.IsDBNull(1) ? null : r.GetString(1),
        ClientIp = r.IsDBNull(2) ? null : r.GetString(2),
        ConfigData = r.GetString(3),
        DiffData = r.IsDBNull(4) ? null : r.GetString(4),
        CreatedBy = r.GetString(5),
        CreatedAt = DateTime.Parse(r.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };
}
