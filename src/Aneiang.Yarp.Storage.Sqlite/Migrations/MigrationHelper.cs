using Microsoft.Data.Sqlite;

namespace Aneiang.Yarp.Storage.Sqlite.Migrations;

/// <summary>
/// Shared helper methods used by migration implementations.
/// </summary>
internal static class MigrationHelper
{
    internal static async Task<bool> ColumnExistsAsync(
        SqliteConnection conn, SqliteTransaction transaction, string table, string column, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info(@table) WHERE name = @column";
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    internal static async Task AddColumnIfNotExistsAsync(
        SqliteConnection conn, SqliteTransaction transaction, string table, string column, string definition, CancellationToken ct)
    {
        if (!await ColumnExistsAsync(conn, transaction, table, column, ct))
            await ExecuteAsync(conn, transaction, $"ALTER TABLE {table} ADD COLUMN {column} {definition}", ct);
    }

    internal static async Task<int> ExecuteAsync(SqliteConnection conn, SqliteTransaction transaction, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandTimeout = 30;
        cmd.CommandText = sql;
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    internal static async Task<int> ExecuteAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = sql;
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    internal static async Task<string?> ExecuteScalarStringAsync(SqliteConnection conn, SqliteTransaction transaction, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandTimeout = 30;
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is DBNull or null ? null : result.ToString();
    }
}
