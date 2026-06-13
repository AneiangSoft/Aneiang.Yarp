namespace Aneiang.Yarp.Storage;

/// <summary>
/// Storage configuration options. Binds from <c>Gateway:Storage</c> config section.
/// Only SQLite is supported as the persistence backend.
/// </summary>
public class StorageOptions
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Gateway:Storage";

    /// <summary>SQLite storage options.</summary>
    public SqliteStorageOptions Sqlite { get; set; } = new();
}

/// <summary>SQLite storage options.</summary>
public class SqliteStorageOptions
{
    /// <summary>
    /// SQLite connection string. Example: <c>Data Source=gateway-store.db</c>.
    /// Supports optional <c>Password</c> parameter for SQLCipher.
    /// </summary>
    public string ConnectionString { get; set; } = "Data Source=gateway-store.db";
}
