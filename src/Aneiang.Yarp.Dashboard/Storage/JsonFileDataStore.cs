using System.Collections.Concurrent;
using System.Text.Json;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Storage;

/// <summary>
/// JSON file-based <see cref="IDataStore"/> implementation.
/// Documents are persisted as individual JSON files under <see cref="JsonFileStorageOptions.BasePath"/>.
/// Collections are stored as JSON arrays in separate files.
/// Thread-safe via per-file locks.
/// </summary>
public class JsonFileDataStore : IDataStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _basePath;
    private readonly ILogger<JsonFileDataStore> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();

    public JsonFileDataStore(StorageOptions options, ILogger<JsonFileDataStore> logger)
    {
        _basePath = string.IsNullOrEmpty(options.JsonFile.BasePath)
            ? AppDomain.CurrentDomain.BaseDirectory
            : options.JsonFile.BasePath;
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_basePath))
            Directory.CreateDirectory(_basePath);

        _logger.LogInformation("JsonFileDataStore initialized at {BasePath}", _basePath);
        return Task.CompletedTask;
    }

    // ── Document operations ──

    public async Task<T?> GetDocumentAsync<T>(string category, CancellationToken ct = default)
    {
        var path = GetDocumentPath(category);
        var lck = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await lck.WaitAsync(ct);
        try
        {
            if (!File.Exists(path))
                return default;

            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read document {Category}", category);
            return default;
        }
        finally
        {
            lck.Release();
        }
    }

    public async Task SetDocumentAsync<T>(string category, T document, CancellationToken ct = default)
    {
        var path = GetDocumentPath(category);
        var lck = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await lck.WaitAsync(ct);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(document, _jsonOptions);
            var tmpPath = path + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, ct);
            File.Move(tmpPath, path, overwrite: true);

            _logger.LogDebug("Document {Category} saved to {Path}", category, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save document {Category}", category);
            throw;
        }
        finally
        {
            lck.Release();
        }
    }

    public async Task DeleteDocumentAsync(string category, CancellationToken ct = default)
    {
        var path = GetDocumentPath(category);
        var lck = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await lck.WaitAsync(ct);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.LogInformation("Document {Category} deleted", category);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document {Category}", category);
            throw;
        }
        finally
        {
            lck.Release();
        }
    }

    public async Task<bool> DocumentExistsAsync(string category, CancellationToken ct = default)
    {
        var path = GetDocumentPath(category);
        var lck = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await lck.WaitAsync(ct);
        try
        {
            return File.Exists(path);
        }
        finally
        {
            lck.Release();
        }
    }

    // ── Collection operations ──

    public async Task<IReadOnlyList<T>> GetCollectionAsync<T>(string category, CancellationToken ct = default)
    {
        var path = GetCollectionPath(category);
        var lck = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await lck.WaitAsync(ct);
        try
        {
            if (!File.Exists(path))
                return Array.Empty<T>();

            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<List<T>>(json, _jsonOptions) ?? new List<T>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read collection {Category}", category);
            return Array.Empty<T>();
        }
        finally
        {
            lck.Release();
        }
    }

    public async Task AddToCollectionAsync<T>(string category, T item, CancellationToken ct = default)
    {
        var path = GetCollectionPath(category);
        var lck = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await lck.WaitAsync(ct);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            List<T> items;
            if (File.Exists(path))
            {
                var existing = await File.ReadAllTextAsync(path, ct);
                items = JsonSerializer.Deserialize<List<T>>(existing, _jsonOptions) ?? new List<T>();
            }
            else
            {
                items = new List<T>();
            }

            items.Add(item);

            var json = JsonSerializer.Serialize(items, _jsonOptions);
            var tmpPath = path + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, ct);
            File.Move(tmpPath, path, overwrite: true);

            _logger.LogDebug("Item added to collection {Category}", category);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add to collection {Category}", category);
            throw;
        }
        finally
        {
            lck.Release();
        }
    }

    public async Task ClearCollectionAsync(string category, CancellationToken ct = default)
    {
        var path = GetCollectionPath(category);
        var lck = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await lck.WaitAsync(ct);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.LogInformation("Collection {Category} cleared", category);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear collection {Category}", category);
            throw;
        }
        finally
        {
            lck.Release();
        }
    }

    public async Task<int> GetCollectionCountAsync(string category, CancellationToken ct = default)
    {
        var items = await GetCollectionAsync<object>(category, ct);
        return items.Count;
    }

    // ── Helpers ──

    private string GetDocumentPath(string category) => Path.Combine(_basePath, $"{category}.json");
    private string GetCollectionPath(string category) => Path.Combine(_basePath, $"{category}.collection.json");

    public void Dispose()
    {
        foreach (var lck in _fileLocks.Values)
            lck.Dispose();
        _fileLocks.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
