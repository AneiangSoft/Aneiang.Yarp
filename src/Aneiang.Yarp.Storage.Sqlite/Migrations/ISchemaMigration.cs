using Microsoft.Data.Sqlite;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>
/// Defines a single schema migration step. Migrations are applied in
/// <see cref="Version"/> order and tracked in the <c>schema_migrations</c> table.
/// </summary>
public interface ISchemaMigration
{
    /// <summary>Numeric version — migrations are applied in ascending order.</summary>
    int Version { get; }

    /// <summary>Unique migration identifier (used in schema_migrations.id).</summary>
    string Id { get; }

    /// <summary>Human-readable description of this migration.</summary>
    string Description { get; }

    /// <summary>Execute the migration DDL/DML inside the provided transaction.</summary>
    Task UpAsync(SqliteConnection conn, SqliteTransaction transaction, CancellationToken ct);
}
