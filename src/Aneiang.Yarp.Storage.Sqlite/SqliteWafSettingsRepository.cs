using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>SQLite implementation of <see cref="IWafSettingsRepository"/>.</summary>
public sealed class SqliteWafSettingsRepository : IWafSettingsRepository
{
    private readonly SqliteConnectionFactory _connections;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteWafSettingsRepository(SqliteConnectionFactory connections) => _connections = connections;

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
        await SqliteRepositoryInitializer.EnsureTableExistsAsync(_connections, "waf_settings", ct);
        _initialized = true;
    }

    public async Task<WafSettingsEntity?> GetWafSettingsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM waf_settings WHERE id = 1";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Map(reader);
    }

    public async Task SaveWafSettingsAsync(WafSettingsEntity settings, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO waf_settings (id, enabled, enable_ip_check, ip_whitelist_json, ip_blacklist_json,
                enable_request_size_validation, max_request_body_size, max_header_count, max_header_size,
                enable_sql_injection_detection, enable_xss_detection, enable_path_traversal_detection,
                extra_script_sources, updated_at)
            VALUES (1, @en, @eic, @iwl, @ibl, @ersv, @mrbs, @mhc, @mhs, @esid, @exss, @eptd, @ess, @ua)
            ON CONFLICT(id) DO UPDATE SET
                enabled = @en, enable_ip_check = @eic, ip_whitelist_json = @iwl, ip_blacklist_json = @ibl,
                enable_request_size_validation = @ersv, max_request_body_size = @mrbs, max_header_count = @mhc,
                max_header_size = @mhs, enable_sql_injection_detection = @esid, enable_xss_detection = @exss,
                enable_path_traversal_detection = @eptd, extra_script_sources = @ess, updated_at = @ua
            """;
        cmd.Parameters.AddWithValue("@en", settings.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@eic", settings.EnableIpCheck ? 1 : 0);
        cmd.Parameters.AddWithValue("@iwl", settings.IpWhitelistJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ibl", settings.IpBlacklistJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ersv", settings.EnableRequestSizeValidation ? 1 : 0);
        cmd.Parameters.AddWithValue("@mrbs", settings.MaxRequestBodySize);
        cmd.Parameters.AddWithValue("@mhc", settings.MaxHeaderCount);
        cmd.Parameters.AddWithValue("@mhs", settings.MaxHeaderSize);
        cmd.Parameters.AddWithValue("@esid", settings.EnableSqlInjectionDetection ? 1 : 0);
        cmd.Parameters.AddWithValue("@exss", settings.EnableXssDetection ? 1 : 0);
        cmd.Parameters.AddWithValue("@eptd", settings.EnablePathTraversalDetection ? 1 : 0);
        cmd.Parameters.AddWithValue("@ess", settings.ExtraScriptSources ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ua", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static WafSettingsEntity Map(SqliteDataReader r) => new()
    {
        Enabled = r.GetInt32(r.GetOrdinal("enabled")) == 1,
        EnableIpCheck = r.GetInt32(r.GetOrdinal("enable_ip_check")) == 1,
        IpWhitelistJson = r.IsDBNull(r.GetOrdinal("ip_whitelist_json")) ? null : r.GetString(r.GetOrdinal("ip_whitelist_json")),
        IpBlacklistJson = r.IsDBNull(r.GetOrdinal("ip_blacklist_json")) ? null : r.GetString(r.GetOrdinal("ip_blacklist_json")),
        EnableRequestSizeValidation = r.GetInt32(r.GetOrdinal("enable_request_size_validation")) == 1,
        MaxRequestBodySize = r.GetInt64(r.GetOrdinal("max_request_body_size")),
        MaxHeaderCount = r.GetInt32(r.GetOrdinal("max_header_count")),
        MaxHeaderSize = r.GetInt32(r.GetOrdinal("max_header_size")),
        EnableSqlInjectionDetection = r.GetInt32(r.GetOrdinal("enable_sql_injection_detection")) == 1,
        EnableXssDetection = r.GetInt32(r.GetOrdinal("enable_xss_detection")) == 1,
        EnablePathTraversalDetection = r.GetInt32(r.GetOrdinal("enable_path_traversal_detection")) == 1,
        ExtraScriptSources = r.IsDBNull(r.GetOrdinal("extra_script_sources")) ? null : r.GetString(r.GetOrdinal("extra_script_sources")),
        UpdatedAt = DateTime.Parse(r.GetString(r.GetOrdinal("updated_at")))
    };
}
