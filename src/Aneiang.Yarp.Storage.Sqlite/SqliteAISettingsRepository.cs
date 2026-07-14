using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>
/// SQLite implementation of IAISettingsRepository.
/// Reads/writes key-value pairs from the ai_settings table.
/// </summary>
public sealed class SqliteAISettingsRepository : IAISettingsRepository
{
    private readonly SqliteConnectionFactory _connections;
    private readonly ILogger<SqliteAISettingsRepository> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteAISettingsRepository(SqliteConnectionFactory connections, ILogger<SqliteAISettingsRepository> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            await SqliteRepositoryInitializer.EnsureTableExistsAsync(_connections, "ai_settings", ct);
            _initialized = true;
        }
        finally { _initLock.Release(); }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> LoadAllAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var result = new Dictionary<string, string>();

        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM ai_settings";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string key, string value, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ai_settings (Key, Value)
            VALUES (@key, @value)
            ON CONFLICT(Key) DO UPDATE SET Value = @value
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc />
    public async Task SaveBatchAsync(IEnumerable<(string Key, string Value)> pairs, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = conn.BeginTransaction();

        foreach (var (key, value) in pairs)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO ai_settings (Key, Value)
                VALUES (@key, @value)
                ON CONFLICT(Key) DO UPDATE SET Value = @value
                """;
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ai_settings";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
