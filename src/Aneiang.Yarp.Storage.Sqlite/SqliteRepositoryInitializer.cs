namespace Aneiang.Yarp.Storage.Sqlite;

internal static class SqliteRepositoryInitializer
{
    public static async Task EnsureTableExistsAsync(SqliteConnectionFactory connections, string tableName, CancellationToken ct)
    {
        await using var conn = connections.CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
        cmd.Parameters.AddWithValue("@name", tableName);
        var exists = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
        if (!exists)
        {
            throw new InvalidOperationException($"SQLite schema is not initialized. Missing table: {tableName}");
        }
    }
}
