namespace Aneiang.Yarp.Storage;

/// <summary>
/// Unified document-level data store interface supporting multiple persistence backends
/// (JSON file, SQLite, Redis). Provides key-value document and collection operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> General-purpose document storage (key → value, category → list).</para>
/// <para><b>When to use:</b> For simple document/collection CRUD where a flat key-category model suffices.
/// Used by the core <c>Aneiang.Yarp</c> package for config file persistence.</para>
/// <para><b>Relationship with <see cref="IStructuredDataStore"/>:</b>
/// <c>IDataStore</c> is the lower-level, schema-agnostic abstraction used by
/// <c>IDynamicConfigPersistenceService</c>. <c>IStructuredDataStore</c> is the higher-level,
/// schema-aware abstraction with dedicated entity types for route/cluster/policy/audit tables.
/// Both may be backed by the same physical store (SQLite, Redis) but serve different layers.</para>
/// <para><b>Backends:</b> JsonFileDataStore, SqliteDataStore, RedisDataStore.</para>
/// </remarks>
public interface IDataStore : IAsyncDisposable, IDisposable
{
    /// <summary>Initialize the store (create tables, connections, directories, etc.).</summary>
    Task InitializeAsync(CancellationToken ct = default);

    // ── Document operations (single document per category) ──

    /// <summary>Get a document by category. Returns default(T) if not found.</summary>
    Task<T?> GetDocumentAsync<T>(string category, CancellationToken ct = default);

    /// <summary>Set or replace a document for a category.</summary>
    Task SetDocumentAsync<T>(string category, T document, CancellationToken ct = default);

    /// <summary>Delete a document by category.</summary>
    Task DeleteDocumentAsync(string category, CancellationToken ct = default);

    /// <summary>Check if a document exists.</summary>
    Task<bool> DocumentExistsAsync(string category, CancellationToken ct = default);

    // ── Collection operations (multiple items per category) ──

    /// <summary>Get all items in a collection.</summary>
    Task<IReadOnlyList<T>> GetCollectionAsync<T>(string category, CancellationToken ct = default);

    /// <summary>Add an item to a collection.</summary>
    Task AddToCollectionAsync<T>(string category, T item, CancellationToken ct = default);

    /// <summary>Clear all items in a collection.</summary>
    Task ClearCollectionAsync(string category, CancellationToken ct = default);

    /// <summary>Get the count of items in a collection.</summary>
    Task<int> GetCollectionCountAsync(string category, CancellationToken ct = default);
}
