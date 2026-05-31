using System.Collections.Concurrent;
using System.Text.Json;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Services.Implements;

/// <summary>
/// Audit log store for gateway configuration changes.
/// Uses in-memory <see cref="ConcurrentQueue{T}"/> for fast writes,
/// and persists to <see cref="IDataStore"/> so entries survive restarts.
/// Thread-safe, bounded by max capacity (ring-buffer style).
/// </summary>
public class ConfigChangeAuditLog : IConfigChangeAuditLog
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private const string Category = "audit-log";
    private const int MaxCapacity = 200;

    private readonly ConcurrentQueue<ConfigChangeAudit> _entries = new();
    private int _totalCount;
    private long _evictedCount;
    private bool _loaded;

    private readonly IDataStore _store;
    private readonly ILogger<ConfigChangeAuditLog> _logger;

    /// <summary>
    /// Raised when a config change audit entry is recorded (success only).
    /// </summary>
    public event Action<string, string, string?, object?>? OnConfigChanged;

    public ConfigChangeAuditLog(IDataStore store, ILogger<ConfigChangeAuditLog> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>Load persisted entries from store on first access.</summary>
    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        lock (_entries)
        {
            if (_loaded) return; // Double-check locking
            _loaded = true;
        }

        try
        {
            var persisted = await _store.GetCollectionAsync<ConfigChangeAudit>(Category);
            foreach (var entry in persisted)
            {
                _entries.Enqueue(entry);
                Interlocked.Increment(ref _totalCount);
            }
            _logger.LogDebug("Loaded {Count} audit entries from store", persisted.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load persisted audit entries");
        }
    }

    /// <summary>Non-blocking ensure loaded for synchronous contexts.</summary>
    private void EnsureLoaded()
    {
        if (_loaded) return;

        // Fire-and-forget initialization with exception handling
        // This avoids blocking the thread while loading from persistent store
        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureLoadedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background audit log initialization failed");
            }
        });
    }

    /// <inheritdoc />
    public void Record(ConfigChangeAudit entry)
    {
        // Non-blocking initialization - if not loaded, start loading in background
        // Entries recorded before load completes will be in memory but not persisted until next load
        EnsureLoaded();

        _entries.Enqueue(entry);
        var count = Interlocked.Increment(ref _totalCount);

        while (count > MaxCapacity)
        {
            if (_entries.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _totalCount);
                Interlocked.Increment(ref _evictedCount);
                count--;
            }
            else break;
        }

        _logger.LogDebug("Audit: {Action} on '{Target}' by {Operator} - {Status}",
            entry.Action, entry.Target, entry.Operator ?? "unknown",
            entry.Success ? "OK" : $"FAIL: {entry.ErrorMessage}");

        // Persist to store (fire-and-forget with error handling)
        _ = Task.Run(async () =>
        {
            try
            {
                await _store.AddToCollectionAsync(Category, entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist audit entry");
            }
        });

        if (entry.Success)
        {
            OnConfigChanged?.Invoke(entry.Action, entry.Target, entry.Operator, entry.After);
        }
    }

    /// <inheritdoc />
    public void RecordSuccess(string action, string target, string? operatorName = null, string? clientIp = null,
        object? before = null, object? after = null)
    {
        Record(new ConfigChangeAudit
        {
            Action = action,
            Target = target,
            Operator = operatorName,
            ClientIp = clientIp,
            Before = before != null ? JsonSerializer.Serialize(before, _jsonOptions) : null,
            After = after != null ? JsonSerializer.Serialize(after, _jsonOptions) : null,
            Success = true
        });
    }

    /// <inheritdoc />
    public void RecordFailure(string action, string target, string errorMessage, string? operatorName = null)
    {
        Record(new ConfigChangeAudit
        {
            Action = action,
            Target = target,
            Operator = operatorName,
            Success = false,
            ErrorMessage = errorMessage
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<ConfigChangeAudit> GetRecent(int count = 50)
    {
        // Non-blocking - start loading in background if needed
        EnsureLoaded();

        // Return entries without sorting if not many entries (optimization)
        var entries = _entries.ToArray();
        if (entries.Length <= 1)
            return entries.Take(count).ToList();

        // Use Array.Sort for better performance than OrderByDescending on small collections
        Array.Sort(entries, (a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return entries.Take(count).ToList();
    }

    /// <inheritdoc />
    public int Count => _entries.Count;

    /// <inheritdoc />
    public long EvictedCount => Volatile.Read(ref _evictedCount);
}
