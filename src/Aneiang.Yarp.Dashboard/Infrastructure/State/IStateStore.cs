using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.State;

/// <summary>
/// Abstract key-value state store for persisting runtime state
/// (plugin states, 2FA state, etc.).
/// Default implementation uses the local file system; can be replaced
/// with a database-backed implementation for containerized deployments.
/// </summary>
public interface IStateStore
{
    /// <summary>Load a typed value by key. Returns default if not found.</summary>
    Task<T?> LoadAsync<T>(string key, CancellationToken ct = default);

    /// <summary>Save a value by key.</summary>
    Task SaveAsync<T>(string key, T value, CancellationToken ct = default);

    /// <summary>Delete a key.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Check if a key exists.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// Default file-system implementation of <see cref="IStateStore"/>.
/// Each key maps to a JSON file under the configured base directory.
/// Thread-safe via per-key locking.
/// </summary>
public class FileStateStore : IStateStore
{
    private readonly string _baseDirectory;
    private readonly ILogger<FileStateStore> _logger;
    private readonly Dictionary<string, SemaphoreSlim> _locks = new();
    private readonly object _locksLock = new();

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public FileStateStore(string baseDirectory, ILogger<FileStateStore> logger)
    {
        _baseDirectory = baseDirectory;
        _logger = logger;

        if (!Directory.Exists(_baseDirectory))
            Directory.CreateDirectory(_baseDirectory);
    }

    public async Task<T?> LoadAsync<T>(string key, CancellationToken ct = default)
    {
        var filePath = GetFilePath(key);

        try
        {
            if (!File.Exists(filePath))
                return default;

            var json = await File.ReadAllTextAsync(filePath, ct);
            return JsonSerializer.Deserialize<T>(json, _jsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load state for key '{Key}' from {Path}", key, filePath);
            return default;
        }
    }

    public async Task SaveAsync<T>(string key, T value, CancellationToken ct = default)
    {
        var filePath = GetFilePath(key);
        var semaphore = GetLock(key);

        await semaphore.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(value, _jsonOpts);
            await File.WriteAllTextAsync(filePath, json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save state for key '{Key}' to {Path}", key, filePath);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var filePath = GetFilePath(key);

        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete state for key '{Key}' at {Path}", key, filePath);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        var filePath = GetFilePath(key);
        return Task.FromResult(File.Exists(filePath));
    }

    private string GetFilePath(string key)
    {
        // Sanitize key to prevent path traversal
        var safeKey = string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));
        return Path.Combine(_baseDirectory, $"{safeKey}.json");
    }

    private SemaphoreSlim GetLock(string key)
    {
        lock (_locksLock)
        {
            if (!_locks.TryGetValue(key, out var semaphore))
            {
                semaphore = new SemaphoreSlim(1, 1);
                _locks[key] = semaphore;
            }
            return semaphore;
        }
    }
}
