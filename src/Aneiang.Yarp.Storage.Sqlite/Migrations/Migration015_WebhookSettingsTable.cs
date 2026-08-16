using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>Webhook settings key-value table for Dashboard notification persistence.</summary>
internal sealed class Migration015_WebhookSettingsTable : ISchemaMigration
{
    public int Version => 15;
    public string Id => "015_webhook_settings_table";
    public string Description => "Create webhook settings table for Dashboard webhook notification persistence";

    public Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
        => ExecuteAsync(conn, transaction, """
            CREATE TABLE IF NOT EXISTS webhook_settings (
                Key             TEXT PRIMARY KEY,
                Value           TEXT NOT NULL,
                UpdatedAt       TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """, ct);
}
