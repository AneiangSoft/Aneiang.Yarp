using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Aneiang.Yarp.Storage.Sqlite;

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
        await SqliteRepositoryInitializer.EnsureTableExistsAsync(_connections, "gateway_policies", ct);
        await SqliteRepositoryInitializer.EnsureTableExistsAsync(_connections, "policy_targets", ct);
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
        policy.UpdatedAt = DateTime.Now;
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO gateway_policies (policy_uid, policy_id, policy_type, display_name, description, enabled,
                retry_config, rate_limit_config, circuit_breaker_config, waf_enabled,
                created_at, updated_at)
            VALUES (@uid, @id, @type, @name, @desc, @en, @retry, @rl, @cb, @waf, @ca, @ua)
            ON CONFLICT(policy_id) DO UPDATE SET
                policy_uid = @uid, policy_type = @type, display_name = @name, description = @desc, enabled = @en,
                retry_config = @retry, rate_limit_config = @rl, circuit_breaker_config = @cb,
                waf_enabled = @waf, updated_at = @ua
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

    public async Task<IReadOnlyList<PolicyTargetEntity>> GetAllPolicyTargetsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = _connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM policy_targets ORDER BY policy_id, target_type, created_at";
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

    private static string? ReadString(SqliteDataReader r, string name)
    {
        var ordinal = r.GetOrdinal(name);
        return r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }

    private static DateTime? ReadDateTime(SqliteDataReader r, string name)
    {
        var value = ReadString(r, name);
        return string.IsNullOrEmpty(value) ? null : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

}
