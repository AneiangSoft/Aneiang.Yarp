using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>Proxy log, notification, and WAF event tables.</summary>
internal sealed class Migration003_LogAndNotificationTables : ISchemaMigration
{
    public int Version => 3;
    public string Id => "003_log_notification_tables";
    public string Description => "Create proxy log, notification and WAF event tables";

    public Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
        => ExecuteAsync(conn, transaction, """
            CREATE TABLE IF NOT EXISTS proxy_logs (
                id TEXT PRIMARY KEY,
                method TEXT,
                path TEXT,
                route_id TEXT,
                cluster_id TEXT,
                destination_id TEXT,
                status_code INTEGER DEFAULT 0,
                duration_ms INTEGER DEFAULT 0,
                request_body_size INTEGER,
                response_body_size INTEGER,
                client_ip TEXT,
                error_message TEXT,
                timestamp TEXT NOT NULL
            );
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
            CREATE TABLE IF NOT EXISTS notification_settings (
                id TEXT PRIMARY KEY DEFAULT 'notification_settings',
                enabled INTEGER DEFAULT 1,
                channels TEXT,
                rules TEXT,
                global_settings TEXT,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS proxy_logs_meta (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp       TEXT NOT NULL,
                EventType       TEXT NOT NULL,
                Level           TEXT NOT NULL,
                RouteId         TEXT,
                ClusterId       TEXT,
                Method          TEXT,
                UpstreamPath    TEXT,
                StatusCode      INTEGER DEFAULT 0,
                ElapsedMs       REAL DEFAULT 0,
                TraceId         TEXT,
                HasRequestBody  INTEGER DEFAULT 0,
                HasResponseBody INTEGER DEFAULT 0,
                DownstreamUrl   TEXT
            );
            CREATE TABLE IF NOT EXISTS proxy_logs_body (
                MetaId          INTEGER PRIMARY KEY REFERENCES proxy_logs_meta(Id) ON DELETE CASCADE,
                Message         TEXT,
                RequestBody     TEXT,
                ResponseBody    TEXT,
                RequestHeaders  TEXT,
                ResponseHeaders TEXT,
                DownstreamBody  TEXT,
                Exception       TEXT
            );
            CREATE TABLE IF NOT EXISTS waf_events_meta (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                EventId         TEXT NOT NULL,
                Timestamp       TEXT NOT NULL,
                ClientIp        TEXT NOT NULL,
                EventType       TEXT NOT NULL,
                RuleName        TEXT NOT NULL,
                RequestUri      TEXT,
                RequestMethod   TEXT,
                RouteUid        TEXT,
                RouteKeySnapshot TEXT,
                ClusterUid      TEXT,
                ClusterKeySnapshot TEXT,
                MatchedValue    TEXT,
                Blocked         INTEGER DEFAULT 1,
                StatusCode      INTEGER
            );
            CREATE TABLE IF NOT EXISTS proxy_log_settings (
                Key             TEXT PRIMARY KEY,
                Value           TEXT NOT NULL
            );
            """, ct);
}
