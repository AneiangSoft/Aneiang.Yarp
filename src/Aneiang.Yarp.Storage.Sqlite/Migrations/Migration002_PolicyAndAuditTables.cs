using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>Policy, audit, config history, and WAF settings tables.</summary>
internal sealed class Migration002_PolicyAndAuditTables : ISchemaMigration
{
    public int Version => 2;
    public string Id => "002_policy_audit_tables";
    public string Description => "Create policy, audit log, config history and WAF settings tables";

    public Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
        => ExecuteAsync(conn, transaction, """
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
            CREATE TABLE IF NOT EXISTS config_audit_logs (
                id TEXT PRIMARY KEY,
                action TEXT NOT NULL,
                target TEXT NOT NULL,
                target_type TEXT,
                operator TEXT,
                client_ip TEXT,
                before_data TEXT,
                after_data TEXT,
                success INTEGER DEFAULT 1,
                error_message TEXT,
                timestamp TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS yarp_config_history (
                version_id TEXT PRIMARY KEY,
                description TEXT,
                client_ip TEXT,
                config_data TEXT NOT NULL,
                diff_data TEXT,
                created_by TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS waf_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                enabled INTEGER DEFAULT 0,
                enable_ip_check INTEGER DEFAULT 1,
                ip_whitelist_json TEXT,
                ip_blacklist_json TEXT,
                enable_request_size_validation INTEGER DEFAULT 1,
                max_request_body_size INTEGER DEFAULT 10485760,
                max_header_count INTEGER DEFAULT 64,
                max_header_size INTEGER DEFAULT 8192,
                enable_sql_injection_detection INTEGER DEFAULT 1,
                enable_xss_detection INTEGER DEFAULT 1,
                enable_path_traversal_detection INTEGER DEFAULT 1,
                extra_script_sources TEXT,
                updated_at TEXT NOT NULL
            );
            """, ct);
}
