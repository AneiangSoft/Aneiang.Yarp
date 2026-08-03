using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace Aneiang.Yarp.Dashboard.Modules.Plugins.Runtime;

public interface IDistributedRateLimitBackend
{
    string Name { get; }
    ValueTask<int> IncrementAsync(string key, DateTimeOffset expiresAt, CancellationToken cancellationToken);
}

public sealed class MemoryDistributedRateLimitBackend : IDistributedRateLimitBackend
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    public string Name => "Memory";

    public ValueTask<int> IncrementAsync(string key, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = _entries.AddOrUpdate(key, _ => new Entry(1, expiresAt), (_, current) =>
            current.ExpiresAt <= now ? new Entry(1, expiresAt) : current with { Count = current.Count + 1 });
        if (_entries.Count > 10_000)
            foreach (var stale in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).Take(1000))
                _entries.TryRemove(stale, out _);
        return ValueTask.FromResult(entry.Count);
    }

    private sealed record Entry(int Count, DateTimeOffset ExpiresAt);
}

public sealed class SqliteDistributedRateLimitBackend : IDistributedRateLimitBackend
{
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;
    public string Name => "Sqlite";

    public async ValueTask<int> IncrementAsync(string key, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var connectionString = Environment.GetEnvironmentVariable("ANEIANG_RATE_LIMIT_SQLITE")
            ?? "Data Source=aneiang-rate-limit.db;Mode=ReadWriteCreate;Cache=Shared;Pooling=True";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureInitializedAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO distributed_rate_limit (bucket_key, count, expires_at) VALUES ($key, 1, $expires)
            ON CONFLICT(bucket_key) DO UPDATE SET
              count = CASE WHEN distributed_rate_limit.expires_at <= $now THEN 1 ELSE distributed_rate_limit.count + 1 END,
              expires_at = CASE WHEN distributed_rate_limit.expires_at <= $now THEN $expires ELSE distributed_rate_limit.expires_at END
            RETURNING count;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$expires", expiresAt.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$now", now);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return count;
    }

    private async Task EnsureInitializedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initializeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA busy_timeout=5000;
                CREATE TABLE IF NOT EXISTS distributed_rate_limit (
                  bucket_key TEXT PRIMARY KEY,
                  count INTEGER NOT NULL,
                  expires_at INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_distributed_rate_limit_expires_at ON distributed_rate_limit(expires_at);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally { _initializeLock.Release(); }
    }
}

public sealed class DistributedRateLimitBackendResolver(IEnumerable<IDistributedRateLimitBackend> backends)
{
    private readonly IReadOnlyDictionary<string, IDistributedRateLimitBackend> _backends = backends.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

    public IDistributedRateLimitBackend Resolve(string name) => _backends.TryGetValue(name, out var backend)
        ? backend
        : throw new InvalidOperationException($"Unknown distributed rate limit backend '{name}'.");
}
