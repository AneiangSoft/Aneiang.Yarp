using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>AI runtime settings key-value table for Dashboard persistence.</summary>
internal sealed class Migration009_AISettingsTable : ISchemaMigration
{
    public int Version => 9;
    public string Id => "009_ai_settings_table";
    public string Description => "Create AI settings table for Dashboard runtime configuration persistence";

    public Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
        => ExecuteAsync(conn, transaction, """
            CREATE TABLE IF NOT EXISTS ai_settings (
                Key             TEXT PRIMARY KEY,
                Value           TEXT NOT NULL
            );
            """, ct);
}
