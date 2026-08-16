using System.Text.Json;
using System.Text.Json.Serialization;
using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>
/// SQLite implementation of <see cref="IWebhookSettingsRepository"/>.
/// Stores the whole settings document as a single JSON row in the
/// <c>webhook_settings</c> key-value table (created by Migration015).
/// </summary>
public sealed class SqliteWebhookSettingsRepository : IWebhookSettingsRepository
{
    private const string SettingsKey = "settings";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly SqliteConnectionFactory _connections;
    private readonly ILogger<SqliteWebhookSettingsRepository> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteWebhookSettingsRepository(SqliteConnectionFactory connections, ILogger<SqliteWebhookSettingsRepository> logger)
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
            await SqliteRepositoryInitializer.EnsureTableExistsAsync(_connections, "webhook_settings", ct);
            _initialized = true;
        }
        finally { _initLock.Release(); }
    }

    /// <inheritdoc />
    public async Task<WebhookSettingsData> LoadAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM webhook_settings WHERE Key = @key";
        cmd.Parameters.AddWithValue("@key", SettingsKey);

        var json = await cmd.ExecuteScalarAsync(ct) as string;
        if (string.IsNullOrWhiteSpace(json))
            return new WebhookSettingsData();

        try
        {
            return JsonSerializer.Deserialize<WebhookSettingsData>(json, _jsonOptions)
                   ?? new WebhookSettingsData();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Webhook settings JSON is corrupted - returning empty settings");
            return new WebhookSettingsData();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(WebhookSettingsData settings, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        ArgumentNullException.ThrowIfNull(settings);

        var json = JsonSerializer.Serialize(settings, _jsonOptions);

        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO webhook_settings (Key, Value, UpdatedAt)
            VALUES (@key, @value, datetime('now'))
            ON CONFLICT(Key) DO UPDATE SET Value = @value, UpdatedAt = datetime('now')
            """;
        cmd.Parameters.AddWithValue("@key", SettingsKey);
        cmd.Parameters.AddWithValue("@value", json);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
