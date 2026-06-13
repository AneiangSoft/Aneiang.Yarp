using System.Text.Json;
using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Storage;

/// <summary>
/// SQLite implementation of <see cref="INotificationRepository"/>.
/// </summary>
public sealed class SqliteNotificationRepository : INotificationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _connectionString;
    private readonly ILogger<SqliteNotificationRepository> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private static bool _providerSet;

    public SqliteNotificationRepository(StorageOptions options, ILogger<SqliteNotificationRepository> logger)
    {
        _connectionString = EnsurePoolingEnabled(options.Sqlite.ConnectionString);
        _logger = logger;
        EnsureProvider();
    }

    private static void EnsureProvider()
    {
        if (_providerSet) return;
        SQLitePCL.Batteries_V2.Init();
        _providerSet = true;
    }

    private static string EnsurePoolingEnabled(string cs)
    {
        if (string.IsNullOrWhiteSpace(cs)) return "Data Source=gateway-store.db;Pooling=true";
        return cs.Contains("Pooling=", StringComparison.OrdinalIgnoreCase) ? cs : cs.TrimEnd(';') + ";Pooling=true";
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            await InitializeAsync(ct);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            -- Notification channels and rules (single row for settings)
            CREATE TABLE IF NOT EXISTS notification_settings (
                id TEXT PRIMARY KEY DEFAULT 'notification_settings',
                enabled INTEGER DEFAULT 1,
                channels TEXT,
                rules TEXT,
                global_settings TEXT,
                updated_at TEXT NOT NULL
            );

            -- Notification history
            CREATE TABLE IF NOT EXISTS notification_history (
                id TEXT PRIMARY KEY,
                event_type TEXT NOT NULL,
                severity INTEGER DEFAULT 0,
                title TEXT NOT NULL,
                message TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                cluster_id TEXT,
                route_id TEXT,
                client_ip TEXT,
                block_reason TEXT,
                request_uri TEXT,
                error_message TEXT,
                attempt_count INTEGER,
                last_status_code INTEGER,
                notified_channels TEXT,
                delivery_success INTEGER DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS ix_notif_history_timestamp ON notification_history(timestamp DESC);
            CREATE INDEX IF NOT EXISTS ix_notif_history_type ON notification_history(event_type);
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        // Ensure default settings row exists
        await using (var upsertCmd = conn.CreateCommand())
        {
            upsertCmd.CommandText = """
                INSERT INTO notification_settings (id, enabled, updated_at)
                VALUES ('notification_settings', 1, @ua)
                ON CONFLICT(id) DO NOTHING
                """;
            upsertCmd.Parameters.AddWithValue("@ua", DateTime.UtcNow.ToString("O"));
            await upsertCmd.ExecuteNonQueryAsync(ct);
        }

        _initialized = true;
        _logger.LogInformation("SqliteNotificationRepository initialized");
    }

    // ─── Settings ────────────────────────────────────────────────────────────────

    public async Task<NotificationSettingsEntity?> LoadSettingsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM notification_settings WHERE id = 'notification_settings'";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new NotificationSettingsEntity
        {
            Id = reader.GetString(0),
            Enabled = reader.GetInt32(1) == 1,
            Channels = reader.IsDBNull(2) ? null : reader.GetString(2),
            Rules = reader.IsDBNull(3) ? null : reader.GetString(3),
            GlobalSettings = reader.IsDBNull(4) ? null : reader.GetString(4),
            UpdatedAt = DateTime.Parse(reader.GetString(5))
        };
    }

    public async Task SaveSettingsAsync(NotificationSettingsEntity settings, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO notification_settings (id, enabled, channels, rules, global_settings, updated_at)
            VALUES ('notification_settings', @en, @ch, @ru, @gs, @ua)
            ON CONFLICT(id) DO UPDATE SET
                enabled = @en, channels = @ch, rules = @ru, global_settings = @gs, updated_at = @ua
            """;
        cmd.Parameters.AddWithValue("@en", settings.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@ch", settings.Channels ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ru", settings.Rules ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@gs", settings.GlobalSettings ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ua", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ─── Channels ──────────────────────────────────────────────────────────────

    public async Task<List<NotificationChannel>> GetChannelsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT channels FROM notification_settings WHERE id = 'notification_settings'";
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is not string json || string.IsNullOrEmpty(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<NotificationChannel>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<NotificationChannel?> GetChannelAsync(string channelId, CancellationToken ct = default)
    {
        var channels = await GetChannelsAsync(ct);
        return channels.FirstOrDefault(c => c.Id == channelId);
    }

    public async Task SaveChannelAsync(NotificationChannel channel, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        channel.UpdatedAt = DateTime.UtcNow;

        var channels = await GetChannelsAsync(ct);
        var existing = channels.FindIndex(c => c.Id == channel.Id);
        if (existing >= 0)
            channels[existing] = channel;
        else
            channels.Add(channel);

        var settings = await LoadSettingsAsync(ct) ?? new NotificationSettingsEntity();
        settings.Channels = JsonSerializer.Serialize(channels, JsonOptions);
        await SaveSettingsAsync(settings, ct);
    }

    public async Task DeleteChannelAsync(string channelId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var channels = await GetChannelsAsync(ct);
        channels.RemoveAll(c => c.Id == channelId);

        var settings = await LoadSettingsAsync(ct) ?? new NotificationSettingsEntity();
        settings.Channels = channels.Count > 0 ? JsonSerializer.Serialize(channels, JsonOptions) : null;
        await SaveSettingsAsync(settings, ct);
    }

    // ─── Rules ────────────────────────────────────────────────────────────────

    public async Task<List<NotificationRule>> GetRulesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT rules FROM notification_settings WHERE id = 'notification_settings'";
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is not string json || string.IsNullOrEmpty(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<NotificationRule>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<NotificationRule?> GetRuleAsync(string ruleId, CancellationToken ct = default)
    {
        var rules = await GetRulesAsync(ct);
        return rules.FirstOrDefault(r => r.Id == ruleId);
    }

    public async Task SaveRuleAsync(NotificationRule rule, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        rule.UpdatedAt = DateTime.UtcNow;

        var rules = await GetRulesAsync(ct);
        var existing = rules.FindIndex(r => r.Id == rule.Id);
        if (existing >= 0)
            rules[existing] = rule;
        else
            rules.Add(rule);

        var settings = await LoadSettingsAsync(ct) ?? new NotificationSettingsEntity();
        settings.Rules = JsonSerializer.Serialize(rules, JsonOptions);
        await SaveSettingsAsync(settings, ct);
    }

    public async Task DeleteRuleAsync(string ruleId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var rules = await GetRulesAsync(ct);
        rules.RemoveAll(r => r.Id == ruleId);

        var settings = await LoadSettingsAsync(ct) ?? new NotificationSettingsEntity();
        settings.Rules = rules.Count > 0 ? JsonSerializer.Serialize(rules, JsonOptions) : null;
        await SaveSettingsAsync(settings, ct);
    }

    // ─── Global Settings ──────────────────────────────────────────────────────

    public async Task<NotificationGlobalSettings> GetGlobalSettingsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT global_settings FROM notification_settings WHERE id = 'notification_settings'";
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is not string json || string.IsNullOrEmpty(json)) return new NotificationGlobalSettings();

        try
        {
            return JsonSerializer.Deserialize<NotificationGlobalSettings>(json, JsonOptions) ?? new NotificationGlobalSettings();
        }
        catch
        {
            return new NotificationGlobalSettings();
        }
    }

    public async Task SaveGlobalSettingsAsync(NotificationGlobalSettings settings, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var entity = await LoadSettingsAsync(ct) ?? new NotificationSettingsEntity();
        entity.GlobalSettings = JsonSerializer.Serialize(settings, JsonOptions);
        await SaveSettingsAsync(entity, ct);
    }

    // ─── History ──────────────────────────────────────────────────────────────

    public async Task RecordNotificationAsync(NotificationHistory record, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO notification_history
            (id, event_type, severity, title, message, timestamp, cluster_id, route_id, client_ip,
             block_reason, request_uri, error_message, attempt_count, last_status_code, notified_channels, delivery_success)
            VALUES (@id, @type, @sev, @title, @msg, @ts, @cid, @rid, @ip, @br, @uri, @err, @att, @status, @nc, @succ)
            """;
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@type", record.EventType);
        cmd.Parameters.AddWithValue("@sev", (int)record.Severity);
        cmd.Parameters.AddWithValue("@title", record.Title);
        cmd.Parameters.AddWithValue("@msg", record.Message);
        cmd.Parameters.AddWithValue("@ts", record.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("@cid", record.ClusterId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@rid", record.RouteId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ip", record.ClientIp ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@br", record.BlockReason ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@uri", record.RequestUri ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@err", record.ErrorMessage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@att", record.AttemptCount ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@status", record.LastStatusCode ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@nc", JsonSerializer.Serialize(record.NotifiedChannels, JsonOptions));
        cmd.Parameters.AddWithValue("@succ", record.DeliverySuccess ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<(List<NotificationHistory> Records, int Total)> GetHistoryAsync(
        int page = 1,
        int pageSize = 100,
        string? eventType = null,
        string? severity = null,
        string? dateStart = null,
        string? dateEnd = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // Count total
        await using var countCmd = conn.CreateCommand();
        var where = BuildWhereClause(eventType, severity, dateStart, dateEnd);
        countCmd.CommandText = $"SELECT COUNT(*) FROM notification_history{where}";
        AddWhereParams(countCmd, eventType, severity, dateStart, dateEnd);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        // Get page
        await using var cmd = conn.CreateCommand();
        var offset = (page - 1) * pageSize;
        cmd.CommandText = $"""
            SELECT * FROM notification_history{where}
            ORDER BY timestamp DESC
            LIMIT @limit OFFSET @offset
            """;
        cmd.Parameters.AddWithValue("@limit", pageSize);
        cmd.Parameters.AddWithValue("@offset", offset);
        AddWhereParams(cmd, eventType, severity, dateStart, dateEnd);

        var records = new List<NotificationHistory>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            records.Add(MapNotificationHistory(reader));
        }

        return (records, total);
    }

    public async Task ClearHistoryAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM notification_history";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static bool IsRealFilterValue(string? value)
        => !string.IsNullOrEmpty(value)
        && !string.Equals(value, "undefined", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase);

    private static string BuildWhereClause(string? eventType, string? severity, string? dateStart, string? dateEnd)
    {
        var conditions = new List<string>();
        if (IsRealFilterValue(eventType)) conditions.Add("event_type = @type");
        // Only add severity condition if it parses as a valid enum value
        // (so SQL never references @sev without the parameter being added).
        if (IsRealFilterValue(severity) && Enum.TryParse<NotificationSeverity>(severity, true, out _))
            conditions.Add("severity = @sev");
        if (IsRealFilterValue(dateStart)) conditions.Add("timestamp >= @dateStart");
        if (IsRealFilterValue(dateEnd)) conditions.Add("timestamp <= @dateEnd");
        return conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
    }

    private static void AddWhereParams(SqliteCommand cmd, string? eventType, string? severity, string? dateStart, string? dateEnd)
    {
        if (IsRealFilterValue(eventType)) cmd.Parameters.AddWithValue("@type", eventType);
        if (IsRealFilterValue(severity) && Enum.TryParse<NotificationSeverity>(severity, true, out var sev))
            cmd.Parameters.AddWithValue("@sev", (int)sev);
        if (IsRealFilterValue(dateStart)) cmd.Parameters.AddWithValue("@dateStart", dateStart);
        if (IsRealFilterValue(dateEnd)) cmd.Parameters.AddWithValue("@dateEnd", dateEnd + "T23:59:59");
    }

    private static NotificationHistory MapNotificationHistory(SqliteDataReader r)
    {
        var notifiedChannelsJson = r.IsDBNull(r.GetOrdinal("notified_channels")) ? null : r.GetString(r.GetOrdinal("notified_channels"));
        List<string> notifiedChannels = [];
        if (!string.IsNullOrEmpty(notifiedChannelsJson))
        {
            try { notifiedChannels = JsonSerializer.Deserialize<List<string>>(notifiedChannelsJson, JsonOptions) ?? []; }
            catch { }
        }

        return new NotificationHistory
        {
            Id = r.GetString(r.GetOrdinal("id")),
            EventType = r.GetString(r.GetOrdinal("event_type")),
            Severity = (NotificationSeverity)r.GetInt32(r.GetOrdinal("severity")),
            Title = r.GetString(r.GetOrdinal("title")),
            Message = r.GetString(r.GetOrdinal("message")),
            Timestamp = DateTime.Parse(r.GetString(r.GetOrdinal("timestamp"))),
            ClusterId = r.IsDBNull(r.GetOrdinal("cluster_id")) ? null : r.GetString(r.GetOrdinal("cluster_id")),
            RouteId = r.IsDBNull(r.GetOrdinal("route_id")) ? null : r.GetString(r.GetOrdinal("route_id")),
            ClientIp = r.IsDBNull(r.GetOrdinal("client_ip")) ? null : r.GetString(r.GetOrdinal("client_ip")),
            BlockReason = r.IsDBNull(r.GetOrdinal("block_reason")) ? null : r.GetString(r.GetOrdinal("block_reason")),
            RequestUri = r.IsDBNull(r.GetOrdinal("request_uri")) ? null : r.GetString(r.GetOrdinal("request_uri")),
            ErrorMessage = r.IsDBNull(r.GetOrdinal("error_message")) ? null : r.GetString(r.GetOrdinal("error_message")),
            AttemptCount = r.IsDBNull(r.GetOrdinal("attempt_count")) ? null : r.GetInt32(r.GetOrdinal("attempt_count")),
            LastStatusCode = r.IsDBNull(r.GetOrdinal("last_status_code")) ? null : r.GetInt32(r.GetOrdinal("last_status_code")),
            NotifiedChannels = notifiedChannels,
            DeliverySuccess = r.GetInt32(r.GetOrdinal("delivery_success")) == 1
        };
    }
}
