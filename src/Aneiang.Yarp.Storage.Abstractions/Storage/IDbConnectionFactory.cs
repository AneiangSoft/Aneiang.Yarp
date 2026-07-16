using System.Data.Common;

namespace Aneiang.Yarp.Storage;

public interface IDbConnectionFactory
{
    DbConnection CreateConnection();

    ValueTask<DbConnection> CreateConnectionAsync(CancellationToken ct = default);
}
