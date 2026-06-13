using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Storage;

/// <summary>SQLite implementation of <see cref="IPolicyRepository"/>.</summary>
public sealed class SqlitePolicyRepository : IPolicyRepository
{
    private readonly SqliteConnectionFactory _connections;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqlitePolicyRepository(SqliteConnectionFactory connections) => _connections = connections;

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
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS gateway_policies (
                policy_id TEXT PRIMARY KEY,
                policy_type TEXT NOT NULL DEFAULT 'route',
                display_name TEXT NOT NULL,
                description TEXT,
                enabled INTEGER DEFAULT 1,
                retry_config TEXT,
                rate_limit_config TEXT,
                circuit_breaker_config TEXT,
                waf_enabled TEXT,
                applied_targets TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_policies_enabled ON gateway_policies(enabled);
            CREATE INDEX IF NOT EXISTS ix_policies_type ON gateway_policies(policy_type);
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        foreach (var migration in new[]
        {
            "ALTER TABLE gateway_policies ADD COLUMN policy_type TEXT NOT NULL DEFAULT 'route'",
            "ALTER TABLE gateway_policies ADD COLUMN waf_enabled TEXT",
            "ALTER TABLE gateway_policies ADD COLUMN applied_targets TEXT"
        })
        {
            try
            {
                await using var mCmd = conn.CreateCommand();
                mCmd.CommandText = migration;
                await mCmd.ExecuteNonQueryAsync(ct);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
        }

        _initialized = true;
    }

    public async Task<PolicyEntity?> GetPolicyAsync(string policyId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM gateway_policies WHERE policy_id = @id";
        cmd.Parameters.AddWithValue("@id", policyId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<PolicyEntity>> GetAllPoliciesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT policy_id, policy_type, display_name, description, enabled,
                   retry_config, rate_limit_config, circuit_breaker_config,
                   waf_enabled, applied_targets, created_at, updated_at
            FROM gateway_policies ORDER BY policy_type, display_name
            """;
        var list = new List<PolicyEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(Map(reader));
        return list.AsReadOnly();
    }

    public async Task SavePolicyAsync(PolicyEntity policy, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        policy.UpdatedAt = DateTime.UtcNow;
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO gateway_policies (policy_id, policy_type, display_name, description, enabled,
                retry_config, rate_limit_config, circuit_breaker_config, waf_enabled, applied_targets,
                created_at, updated_at)
            VALUES (@id, @type, @name, @desc, @en, @retry, @rl, @cb, @waf, @targets, @ca, @ua)
            ON CONFLICT(policy_id) DO UPDATE SET
                policy_type = @type, display_name = @name, description = @desc, enabled = @en,
                retry_config = @retry, rate_limit_config = @rl, circuit_breaker_config = @cb,
                waf_enabled = @waf, applied_targets = @targets, updated_at = @ua
            """;
        cmd.Parameters.AddWithValue("@id", policy.PolicyId);
        cmd.Parameters.AddWithValue("@type", policy.PolicyType);
        cmd.Parameters.AddWithValue("@name", policy.DisplayName);
        cmd.Parameters.AddWithValue("@desc", policy.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@en", policy.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@retry", policy.RetryConfig ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@rl", policy.RateLimitConfig ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cb", policy.CircuitBreakerConfig ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@waf", policy.WafEnabled ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@targets", policy.AppliedTargets ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", policy.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@ua", policy.UpdatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeletePolicyAsync(string policyId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM gateway_policies WHERE policy_id = @id";
        cmd.Parameters.AddWithValue("@id", policyId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static PolicyEntity Map(SqliteDataReader r) => new()
    {
        PolicyId = r.GetString(r.GetOrdinal("policy_id")),
        PolicyType = r.IsDBNull(r.GetOrdinal("policy_type")) ? "route" : r.GetString(r.GetOrdinal("policy_type")),
        DisplayName = r.GetString(r.GetOrdinal("display_name")),
        Description = r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
        Enabled = r.GetInt32(r.GetOrdinal("enabled")) == 1,
        RetryConfig = r.IsDBNull(r.GetOrdinal("retry_config")) ? null : r.GetString(r.GetOrdinal("retry_config")),
        RateLimitConfig = r.IsDBNull(r.GetOrdinal("rate_limit_config")) ? null : r.GetString(r.GetOrdinal("rate_limit_config")),
        CircuitBreakerConfig = r.IsDBNull(r.GetOrdinal("circuit_breaker_config")) ? null : r.GetString(r.GetOrdinal("circuit_breaker_config")),
        WafEnabled = r.IsDBNull(r.GetOrdinal("waf_enabled")) ? null : r.GetString(r.GetOrdinal("waf_enabled")),
        AppliedTargets = r.IsDBNull(r.GetOrdinal("applied_targets")) ? null : r.GetString(r.GetOrdinal("applied_targets")),
        CreatedAt = r.IsDBNull(r.GetOrdinal("created_at")) ? DateTime.MinValue : DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))),
        UpdatedAt = r.IsDBNull(r.GetOrdinal("updated_at")) ? DateTime.MinValue : DateTime.Parse(r.GetString(r.GetOrdinal("updated_at")))
    };
}
