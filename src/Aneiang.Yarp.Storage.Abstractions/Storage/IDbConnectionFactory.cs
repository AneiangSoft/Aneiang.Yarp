using System.Data.Common;

namespace Aneiang.Yarp.Storage;

/// <summary>
/// Abstraction for database connection factories used by storage-agnostic services
/// that need raw SQL access (e.g. WAF event persistence, custom queries).
/// Implementations provide the concrete connection type (SQLite, PostgreSQL, etc.).
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>Creates a new database connection.</summary>
    DbConnection CreateConnection();

    /// <summary>Creates a new database connection (async).</summary>
    ValueTask<DbConnection> CreateConnectionAsync(CancellationToken ct = default);
}
