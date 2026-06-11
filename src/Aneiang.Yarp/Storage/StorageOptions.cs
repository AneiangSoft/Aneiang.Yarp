namespace Aneiang.Yarp.Storage;

/// <summary>Storage provider type.</summary>
public enum StorageProvider
{
    /// <summary>SQLite database (default).</summary>
    Sqlite,

    /// <summary>Redis cache/database.</summary>
    Redis
}

/// <summary>
/// Storage configuration options. Binds from <c>Gateway:Storage</c> config section.
/// </summary>
public class StorageOptions
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Gateway:Storage";

    /// <summary>Storage provider. Default: <see cref="StorageProvider.Sqlite"/>.</summary>
    public StorageProvider Provider { get; set; } = StorageProvider.Sqlite;

    /// <summary>SQLite storage options.</summary>
    public SqliteStorageOptions Sqlite { get; set; } = new();

    /// <summary>Redis storage options.</summary>
    public RedisStorageOptions Redis { get; set; } = new();
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

/// <summary>Redis storage options.</summary>
public class RedisStorageOptions
{
    /// <summary>Redis connection string. Example: <c>localhost:6379,abortConnect=false</c>.</summary>
    public string ConnectionString { get; set; } = "localhost:6379,abortConnect=false";

    /// <summary>Key prefix for all Redis keys. Default: <c>aneiang:</c>.</summary>
    public string Prefix { get; set; } = "aneiang:";

    /// <summary>Redis database index. Default: 0.</summary>
    public int Database { get; set; } = 0;
}
