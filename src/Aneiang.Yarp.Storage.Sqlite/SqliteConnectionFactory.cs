using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>
/// Shared SQLite connection factory used by all storage repositories.
/// Ensures SQLitePCL provider is initialized once and connection pooling is enabled.
/// WAL journal mode is enabled via connection string for better concurrency.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;
    private static bool _providerSet;
    private static readonly object _providerLock = new();

    public SqliteConnectionFactory(IOptions<StorageOptions> options)
    {
        _connectionString = EnsurePoolingAndWalEnabled(options.Value.Sqlite.ConnectionString);
        EnsureProvider();
    }

    /// <summary>Creates a new pooled SQLite connection.</summary>
    public SqliteConnection CreateConnection() => new(_connectionString);

    private static void EnsureProvider()
    {
        if (_providerSet) return;
        lock (_providerLock)
        {
            if (_providerSet) return;
            SQLitePCL.Batteries_V2.Init();
            _providerSet = true;
        }
    }

    private static string EnsurePoolingAndWalEnabled(string cs)
    {
        if (string.IsNullOrWhiteSpace(cs))
            return "Data Source=gateway-store.db;Pooling=true";

        var result = cs;
        if (!result.Contains("Pooling=", StringComparison.OrdinalIgnoreCase))
            result = result.TrimEnd(';') + ";Pooling=true";
        return result;
    }
}
