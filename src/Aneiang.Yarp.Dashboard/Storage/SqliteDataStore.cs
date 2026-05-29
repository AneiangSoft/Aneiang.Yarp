using System.Collections.Concurrent;
using System.Text.Json;
using Aneiang.Yarp.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Storage;

/// <summary>
/// SQLite-based <see cref="IDataStore"/> implementation.
/// Documents are stored in a <c>documents</c> table (category PK → JSON value).
/// Collections are stored in a <c>collections</c> table (auto-increment id, category, JSON value, created_at).
/// Thread-safe via per-category <see cref="SemaphoreSlim"/>.
/// </summary>
public class SqliteDataStore : IDataStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _connectionString;
    private readonly ILogger<SqliteDataStore> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _categoryLocks = new();
    private bool _initialized;
    private static bool _providerSet;

    public SqliteDataStore(StorageOptions options, ILogger<SqliteDataStore> logger)
    {
        _connectionString = options.Sqlite.ConnectionString;
        _logger = logger;
        EnsureProvider();
    }

    /// <summary>
    /// Use SQLCipher provider so that the Password connection string parameter is supported.
    /// </summary>
    private static void EnsureProvider()
    {
        if (_providerSet) return;
        SQLitePCL.Batteries_V2.Init();
        _providerSet = true;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS documents (
                category TEXT PRIMARY KEY,
                value    TEXT NOT NULL,
                type_name TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS collections (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                category   TEXT NOT NULL,
                value      TEXT NOT NULL,
                type_name  TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_collections_category ON collections(category);
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        _initialized = true;
        _logger.LogInformation("SqliteDataStore initialized with connection: {Conn}", _connectionString);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (!_initialized) await InitializeAsync(ct);
    }

    // ── Document operations ──

    public async Task<T?> GetDocumentAsync<T>(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM documents WHERE category = @cat";
        cmd.Parameters.AddWithValue("@cat", category);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull)
            return default;

        return JsonSerializer.Deserialize<T>((string)result, _jsonOptions);
    }

    public async Task SetDocumentAsync<T>(string category, T document, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var lck = _categoryLocks.GetOrAdd(category, _ => new SemaphoreSlim(1, 1));
        await lck.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            var json = JsonSerializer.Serialize(document, _jsonOptions);
            var typeName = typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? typeof(T).Name;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO documents (category, value, type_name, updated_at)
                VALUES (@cat, @val, @type, @ts)
                ON CONFLICT(category) DO UPDATE SET
                    value = @val, type_name = @type, updated_at = @ts
                """;
            cmd.Parameters.AddWithValue("@cat", category);
            cmd.Parameters.AddWithValue("@val", json);
            cmd.Parameters.AddWithValue("@type", typeName);
            cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(ct);
            _logger.LogDebug("Document {Category} saved", category);
        }
        finally
        {
            lck.Release();
        }
    }

    public async Task DeleteDocumentAsync(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM documents WHERE category = @cat";
        cmd.Parameters.AddWithValue("@cat", category);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Document {Category} deleted", category);
    }

    public async Task<bool> DocumentExistsAsync(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM documents WHERE category = @cat";
        cmd.Parameters.AddWithValue("@cat", category);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long count && count > 0;
    }

    // ── Collection operations ──

    public async Task<IReadOnlyList<T>> GetCollectionAsync<T>(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM collections WHERE category = @cat ORDER BY id";
        cmd.Parameters.AddWithValue("@cat", category);

        var items = new List<T>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var json = reader.GetString(0);
            var item = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            if (item is not null)
                items.Add(item);
        }

        return items.AsReadOnly();
    }

    public async Task AddToCollectionAsync<T>(string category, T item, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var lck = _categoryLocks.GetOrAdd(category, _ => new SemaphoreSlim(1, 1));
        await lck.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            var json = JsonSerializer.Serialize(item, _jsonOptions);
            var typeName = typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? typeof(T).Name;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO collections (category, value, type_name, created_at)
                VALUES (@cat, @val, @type, @ts)
                """;
            cmd.Parameters.AddWithValue("@cat", category);
            cmd.Parameters.AddWithValue("@val", json);
            cmd.Parameters.AddWithValue("@type", typeName);
            cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(ct);
            _logger.LogDebug("Item added to collection {Category}", category);
        }
        finally
        {
            lck.Release();
        }
    }

    public async Task ClearCollectionAsync(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM collections WHERE category = @cat";
        cmd.Parameters.AddWithValue("@cat", category);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Collection {Category} cleared", category);
    }

    public async Task<int> GetCollectionCountAsync(string category, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM collections WHERE category = @cat";
        cmd.Parameters.AddWithValue("@cat", category);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long count ? (int)count : 0;
    }

    public void Dispose()
    {
        foreach (var lck in _categoryLocks.Values)
            lck.Dispose();
        _categoryLocks.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
