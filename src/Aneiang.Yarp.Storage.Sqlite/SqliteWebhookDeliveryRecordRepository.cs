using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>
/// SQLite implementation of <see cref="IWebhookDeliveryRecordRepository"/>.
/// Stores a bounded history of webhook delivery attempts in
/// <c>webhook_delivery_records</c> (created by Migration016).
/// </summary>
public sealed class SqliteWebhookDeliveryRecordRepository : IWebhookDeliveryRecordRepository
{
    private readonly SqliteConnectionFactory _connections;
    private readonly ILogger<SqliteWebhookDeliveryRecordRepository> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteWebhookDeliveryRecordRepository(
        SqliteConnectionFactory connections,
        ILogger<SqliteWebhookDeliveryRecordRepository> logger)
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
            await SqliteRepositoryInitializer.EnsureTableExistsAsync(_connections, "webhook_delivery_records", ct);
            _initialized = true;
        }
        finally { _initLock.Release(); }
    }

    /// <inheritdoc />
    public async Task AddAsync(WebhookDeliveryRecord record, int maxRecords = 200, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        ArgumentNullException.ThrowIfNull(record);

        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO webhook_delivery_records
                    (Platform, EventType, Target, Success, HttpStatusCode, Error, DurationMs, CreatedAt)
                VALUES
                    (@platform, @eventType, @target, @success, @status, @error, @duration, datetime('now'))
                """;
            cmd.Parameters.AddWithValue("@platform", record.Platform ?? string.Empty);
            cmd.Parameters.AddWithValue("@eventType", record.EventType ?? string.Empty);
            cmd.Parameters.AddWithValue("@target", (object?)record.Target ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@success", record.Success ? 1 : 0);
            cmd.Parameters.AddWithValue("@status", (object?)record.HttpStatusCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@error", (object?)record.Error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@duration", record.DurationMs);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Prune old rows so the table stays bounded.
        await using (var prune = conn.CreateCommand())
        {
            prune.CommandText = """
                DELETE FROM webhook_delivery_records
                WHERE Id NOT IN (SELECT Id FROM webhook_delivery_records ORDER BY Id DESC LIMIT @max)
                """;
            prune.Parameters.AddWithValue("@max", Math.Max(1, maxRecords));
            await prune.ExecuteNonQueryAsync(ct);
        }
    }

    /// <inheritdoc />
    public async Task<List<WebhookDeliveryRecord>> GetRecentAsync(int limit = 200, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Platform, EventType, Target, Success, HttpStatusCode, Error, DurationMs, CreatedAt
            FROM webhook_delivery_records
            ORDER BY Id DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 1000));

        var result = new List<WebhookDeliveryRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new WebhookDeliveryRecord
            {
                Id = reader.GetInt64(0),
                Platform = reader.GetString(1),
                EventType = reader.GetString(2),
                Target = reader.IsDBNull(3) ? null : reader.GetString(3),
                Success = reader.GetInt32(4) != 0,
                HttpStatusCode = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Error = reader.IsDBNull(6) ? null : reader.GetString(6),
                DurationMs = reader.GetInt32(7),
                CreatedAt = DateTime.TryParse(reader.GetString(8), out var createdAt) ? createdAt : DateTime.UtcNow
            });
        }
        return result;
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM webhook_delivery_records";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
