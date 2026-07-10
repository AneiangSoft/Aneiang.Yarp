using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>
/// Shared SQLite connection factory used by all storage repositories.
/// Ensures SQLitePCL provider is initialized once and connection pooling is enabled.
/// WAL journal mode is enabled via connection string for better concurrency.
/// Schema migration is triggered lazily on the first <see cref="CreateConnection"/> call
/// to guarantee tables exist before any repository reads the database.
/// </summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly IServiceProvider _serviceProvider;
    private static bool _providerSet;
    private static readonly object _providerLock = new();
    private readonly Lazy<Task> _migrationTask;

    public SqliteConnectionFactory(IOptions<StorageOptions> options, IServiceProvider serviceProvider)
    {
        _connectionString = EnsurePoolingAndWalEnabled(options.Value.Sqlite.ConnectionString);
        _serviceProvider = serviceProvider;
        EnsureProvider();

        // Lazy<Task> ensures migration runs exactly once on first connection use.
        // SqliteSchemaMigrator is resolved on-demand to avoid circular DI
        // (migrator depends on SqliteConnectionFactory for connections).
        _migrationTask = new Lazy<Task>(() =>
        {
            var migrator = ActivatorUtilities.CreateInstance<SqliteSchemaMigrator>(
                _serviceProvider, this);
            return migrator.RunMigrationAsync();
        });
    }

    /// <summary>
    /// Creates a new pooled SQLite connection. The first call triggers schema migration
    /// if it has not yet completed, ensuring tables exist before any repository access.
    /// </summary>
    public async ValueTask<SqliteConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        await _migrationTask.Value;
        return new SqliteConnection(_connectionString);
    }

    /// <summary>
    /// Creates a new pooled SQLite connection (synchronous wrapper).
    /// The first call blocks until schema migration completes.
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        // Block on the migration task; subsequent calls return immediately.
        _migrationTask.Value.GetAwaiter().GetResult();
        return new SqliteConnection(_connectionString);
    }

    /// <summary>
    /// Creates a raw connection bypassing migration guard.
    /// Used internally by <see cref="SqliteSchemaMigrator"/> to avoid deadlock
    /// when running migrations.
    /// </summary>
    internal SqliteConnection CreateRawConnection() => new(_connectionString);

    // Explicit interface implementations — return DbConnection for storage-agnostic callers
    DbConnection IDbConnectionFactory.CreateConnection() => CreateConnection();
    async ValueTask<DbConnection> IDbConnectionFactory.CreateConnectionAsync(CancellationToken ct)
        => await CreateConnectionAsync(ct).ConfigureAwait(false);

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
