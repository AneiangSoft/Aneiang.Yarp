using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>Webhook delivery history table for the notification management page.</summary>
internal sealed class Migration016_WebhookDeliveryRecords : ISchemaMigration
{
    public int Version => 16;
    public string Id => "016_webhook_delivery_records";
    public string Description => "Create webhook delivery records table for notification history";

    public Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
        => ExecuteAsync(conn, transaction, """
            CREATE TABLE IF NOT EXISTS webhook_delivery_records (
                Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                Platform       TEXT NOT NULL,
                EventType      TEXT NOT NULL,
                Target         TEXT,
                Success        INTEGER NOT NULL DEFAULT 0,
                HttpStatusCode INTEGER,
                Error          TEXT,
                DurationMs     INTEGER NOT NULL DEFAULT 0,
                CreatedAt      TEXT NOT NULL DEFAULT (datetime('now'))
            );
            CREATE INDEX IF NOT EXISTS idx_webhook_delivery_created
                ON webhook_delivery_records (Id DESC);
            """, ct);
}
