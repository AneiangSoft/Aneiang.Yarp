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
            "ALTER TABLE gateway_policies ADD COLUMN applied_targets TEXT",
            "ALTER TABLE gateway_policies ADD COLUMN policy_uid TEXT"
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

        await using (var backfillCmd = conn.CreateCommand())
        {
            backfillCmd.CommandText = "UPDATE gateway_policies SET policy_uid = lower(hex(randomblob(16))) WHERE policy_uid IS NULL OR policy_uid = ''";
            await backfillCmd.ExecuteNonQueryAsync(ct);
        }
        await using (var indexCmd = conn.CreateCommand())
        {
            indexCmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ix_policies_uid ON gateway_policies(policy_uid)";
            await indexCmd.ExecuteNonQueryAsync(ct);
        }
        await using (var targetCmd = conn.CreateCommand())
        {
            targetCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS policy_targets (
                    id TEXT PRIMARY KEY,
                    policy_uid TEXT NOT NULL,
                    policy_id TEXT NOT NULL,
                    target_type TEXT NOT NULL,
                    target_uid TEXT NOT NULL,
                    target_key_snapshot TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    UNIQUE(policy_uid, target_type, target_uid)
                );
                CREATE INDEX IF NOT EXISTS ix_policy_targets_policy ON policy_targets(policy_id, target_type);
                CREATE INDEX IF NOT EXISTS ix_policy_targets_target ON policy_targets(target_type, target_uid);
                """;
            await targetCmd.ExecuteNonQueryAsync(ct);
        }

        await MigrateAppliedTargetsAsync(conn, ct);
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
        cmd.CommandText = "SELECT * FROM gateway_policies ORDER BY policy_type, display_name";
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
            INSERT INTO gateway_policies (policy_uid, policy_id, policy_type, display_name, description, enabled,
                retry_config, rate_limit_config, circuit_breaker_config, waf_enabled, applied_targets,
                created_at, updated_at)
            VALUES (@uid, @id, @type, @name, @desc, @en, @retry, @rl, @cb, @waf, @targets, @ca, @ua)
            ON CONFLICT(policy_id) DO UPDATE SET
                policy_type = @type, display_name = @name, description = @desc, enabled = @en,
                retry_config = @retry, rate_limit_config = @rl, circuit_breaker_config = @cb,
                waf_enabled = @waf, applied_targets = @targets, updated_at = @ua
            """;
        cmd.Parameters.AddWithValue("@uid", string.IsNullOrWhiteSpace(policy.PolicyUid) ? Guid.NewGuid().ToString("N") : policy.PolicyUid);
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
        await using var tx = conn.BeginTransaction();
        try
        {
            await using (var targetCmd = conn.CreateCommand())
            {
                targetCmd.Transaction = tx;
                targetCmd.CommandText = "DELETE FROM policy_targets WHERE policy_id = @id";
                targetCmd.Parameters.AddWithValue("@id", policyId);
                await targetCmd.ExecuteNonQueryAsync(ct);
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM gateway_policies WHERE policy_id = @id";
                cmd.Parameters.AddWithValue("@id", policyId);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public async Task<IReadOnlyList<PolicyTargetEntity>> GetPolicyTargetsAsync(string policyId, string? targetType = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = string.IsNullOrWhiteSpace(targetType)
            ? "SELECT * FROM policy_targets WHERE policy_id = @pid ORDER BY created_at"
            : "SELECT * FROM policy_targets WHERE policy_id = @pid AND target_type = @type ORDER BY created_at";
        cmd.Parameters.AddWithValue("@pid", policyId);
        if (!string.IsNullOrWhiteSpace(targetType)) cmd.Parameters.AddWithValue("@type", targetType);
        var list = new List<PolicyTargetEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(MapTarget(reader));
        return list.AsReadOnly();
    }

    public async Task SavePolicyTargetAsync(PolicyTargetEntity target, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO policy_targets (id, policy_uid, policy_id, target_type, target_uid, target_key_snapshot, created_at)
            VALUES (@id, @puid, @pid, @type, @tuid, @tkey, @ca)
            ON CONFLICT(policy_uid, target_type, target_uid) DO UPDATE SET
                policy_id = @pid, target_key_snapshot = @tkey
            """;
        cmd.Parameters.AddWithValue("@id", string.IsNullOrWhiteSpace(target.Id) ? Guid.NewGuid().ToString("N") : target.Id);
        cmd.Parameters.AddWithValue("@puid", target.PolicyUid);
        cmd.Parameters.AddWithValue("@pid", target.PolicyId);
        cmd.Parameters.AddWithValue("@type", target.TargetType);
        cmd.Parameters.AddWithValue("@tuid", target.TargetUid);
        cmd.Parameters.AddWithValue("@tkey", target.TargetKeySnapshot);
        cmd.Parameters.AddWithValue("@ca", target.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeletePolicyTargetAsync(string policyId, string targetType, string targetUid, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM policy_targets WHERE policy_id = @pid AND target_type = @type AND target_uid = @tuid";
        cmd.Parameters.AddWithValue("@pid", policyId);
        cmd.Parameters.AddWithValue("@type", targetType);
        cmd.Parameters.AddWithValue("@tuid", targetUid);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeletePolicyTargetsAsync(string policyId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM policy_targets WHERE policy_id = @pid";
        cmd.Parameters.AddWithValue("@pid", policyId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static PolicyEntity Map(SqliteDataReader r) => new()
    {
        PolicyUid = ReadString(r, "policy_uid") ?? Guid.NewGuid().ToString("N"),
        PolicyId = ReadString(r, "policy_id") ?? string.Empty,
        PolicyType = ReadString(r, "policy_type") ?? "route",
        DisplayName = ReadString(r, "display_name") ?? string.Empty,
        Description = ReadString(r, "description"),
        Enabled = r.GetInt32(r.GetOrdinal("enabled")) == 1,
        RetryConfig = ReadString(r, "retry_config"),
        RateLimitConfig = ReadString(r, "rate_limit_config"),
        CircuitBreakerConfig = ReadString(r, "circuit_breaker_config"),
        WafEnabled = ReadString(r, "waf_enabled"),
        AppliedTargets = ReadString(r, "applied_targets"),
        CreatedAt = ReadDateTime(r, "created_at") ?? DateTime.MinValue,
        UpdatedAt = ReadDateTime(r, "updated_at") ?? DateTime.MinValue
    };

    private static PolicyTargetEntity MapTarget(SqliteDataReader r) => new()
    {
        Id = ReadString(r, "id") ?? Guid.NewGuid().ToString("N"),
        PolicyUid = ReadString(r, "policy_uid") ?? string.Empty,
        PolicyId = ReadString(r, "policy_id") ?? string.Empty,
        TargetType = ReadString(r, "target_type") ?? string.Empty,
        TargetUid = ReadString(r, "target_uid") ?? string.Empty,
        TargetKeySnapshot = ReadString(r, "target_key_snapshot") ?? string.Empty,
        CreatedAt = ReadDateTime(r, "created_at") ?? DateTime.MinValue
    };

    private static async Task MigrateAppliedTargetsAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM policy_targets";
        var existingCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
        if (existingCount > 0) return;

        var policies = new List<PolicyEntity>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM gateway_policies WHERE applied_targets IS NOT NULL AND applied_targets <> ''";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) policies.Add(Map(reader));
        }

        foreach (var policy in policies)
        {
            List<string>? targets;
            try { targets = System.Text.Json.JsonSerializer.Deserialize<List<string>>(policy.AppliedTargets ?? "[]"); }
            catch { continue; }
            if (targets == null) continue;

            foreach (var targetKey in targets.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var targetUid = StableUidFromKey(policy.PolicyType, targetKey);
                var target = new PolicyTargetEntity
                {
                    PolicyUid = policy.PolicyUid,
                    PolicyId = policy.PolicyId,
                    TargetType = policy.PolicyType,
                    TargetUid = targetUid,
                    TargetKeySnapshot = targetKey,
                    CreatedAt = policy.UpdatedAt == DateTime.MinValue ? DateTime.UtcNow : policy.UpdatedAt
                };
                await using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = """
                    INSERT OR IGNORE INTO policy_targets (id, policy_uid, policy_id, target_type, target_uid, target_key_snapshot, created_at)
                    VALUES (@id, @puid, @pid, @type, @tuid, @tkey, @ca)
                    """;
                insertCmd.Parameters.AddWithValue("@id", target.Id);
                insertCmd.Parameters.AddWithValue("@puid", target.PolicyUid);
                insertCmd.Parameters.AddWithValue("@pid", target.PolicyId);
                insertCmd.Parameters.AddWithValue("@type", target.TargetType);
                insertCmd.Parameters.AddWithValue("@tuid", target.TargetUid);
                insertCmd.Parameters.AddWithValue("@tkey", target.TargetKeySnapshot);
                insertCmd.Parameters.AddWithValue("@ca", target.CreatedAt.ToString("O"));
                await insertCmd.ExecuteNonQueryAsync(ct);
            }
        }
    }

    private static string? ReadString(SqliteDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }

    private static DateTime? ReadDateTime(SqliteDataReader r, string name)
    {
        var value = ReadString(r, name);
        return string.IsNullOrEmpty(value) ? null : DateTime.Parse(value);
    }

    private static string StableUidFromKey(string prefix, string key)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(prefix + ":" + key));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }
}
