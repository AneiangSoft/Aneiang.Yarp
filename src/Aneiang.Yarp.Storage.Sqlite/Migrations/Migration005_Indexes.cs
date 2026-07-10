using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>All indexes for performance.</summary>
internal sealed class Migration005_Indexes : ISchemaMigration
{
    public int Version => 5;
    public string Id => "005_indexes";
    public string Description => "Create all performance indexes";

    public Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
        => ExecuteAsync(conn, transaction, """
            CREATE INDEX IF NOT EXISTS ix_routes_cluster ON yarp_routes(cluster_id);
            CREATE INDEX IF NOT EXISTS ix_destinations_cluster ON yarp_destinations(cluster_id);
            CREATE INDEX IF NOT EXISTS ix_audit_timestamp ON config_audit_logs(timestamp DESC);
            CREATE INDEX IF NOT EXISTS ix_audit_target ON config_audit_logs(target);
            CREATE INDEX IF NOT EXISTS ix_proxy_logs_timestamp ON proxy_logs(timestamp DESC);
            CREATE INDEX IF NOT EXISTS ix_config_history_created ON yarp_config_history(created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_notif_history_timestamp ON notification_history(timestamp DESC);
            CREATE INDEX IF NOT EXISTS ix_notif_history_type ON notification_history(event_type);
            CREATE INDEX IF NOT EXISTS ix_policies_enabled ON gateway_policies(enabled);
            CREATE INDEX IF NOT EXISTS ix_policies_type ON gateway_policies(policy_type);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_clusters_uid ON yarp_clusters(cluster_uid);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_routes_uid ON yarp_routes(route_uid);
            CREATE INDEX IF NOT EXISTS ix_routes_cluster_uid ON yarp_routes(cluster_uid);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_policies_uid ON gateway_policies(policy_uid);
            CREATE INDEX IF NOT EXISTS ix_policy_targets_policy ON policy_targets(policy_id, target_type);
            CREATE INDEX IF NOT EXISTS ix_policy_targets_target ON policy_targets(target_type, target_uid);
            CREATE INDEX IF NOT EXISTS ix_proxy_logs_meta_time ON proxy_logs_meta(Timestamp);
            CREATE INDEX IF NOT EXISTS ix_proxy_logs_meta_route ON proxy_logs_meta(RouteId);
            CREATE INDEX IF NOT EXISTS ix_proxy_logs_meta_cluster ON proxy_logs_meta(ClusterId);
            CREATE INDEX IF NOT EXISTS ix_proxy_logs_meta_status ON proxy_logs_meta(StatusCode);
            CREATE INDEX IF NOT EXISTS ix_proxy_logs_meta_trace ON proxy_logs_meta(TraceId);
            CREATE INDEX IF NOT EXISTS ix_proxy_logs_meta_event ON proxy_logs_meta(EventType);
            CREATE INDEX IF NOT EXISTS ix_waf_events_time ON waf_events_meta(Timestamp);
            CREATE INDEX IF NOT EXISTS ix_waf_events_type ON waf_events_meta(EventType);
            CREATE INDEX IF NOT EXISTS ix_waf_events_client ON waf_events_meta(ClientIp);
            """, ct);
}
