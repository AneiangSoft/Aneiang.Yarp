using Microsoft.Data.Sqlite;
using static Aneiang.Yarp.Storage.Sqlite.Migrations.MigrationHelper;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>Adds the native YARP destination health endpoint address.</summary>
internal sealed class Migration011_DestinationHealthAddress : ISchemaMigration
{
    public int Version => 11;
    public string Id => "011_destination_health_address";
    public string Description => "Add destination health endpoint address";

    public Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct)
        => AddColumnIfNotExistsAsync(conn, transaction, "yarp_destinations", "health", "TEXT", ct);
}
