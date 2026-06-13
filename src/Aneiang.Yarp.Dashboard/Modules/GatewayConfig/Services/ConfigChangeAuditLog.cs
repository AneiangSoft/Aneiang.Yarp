using System.Collections.Concurrent;
using System.Text.Json;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Audit log store for gateway configuration changes using <see cref="IGatewayRepository"/>.
/// Uses in-memory <see cref="ConcurrentQueue{T}"/> for fast writes,
/// and persists to repository for structured query capability.
/// Thread-safe, bounded by max capacity (ring-buffer style).
///
/// Config-change events are queued and dispatched by <see cref="ConfigChangeEventDispatcher"/>
/// to guarantee delivery even when the first request arrives before all consumers are constructed.
/// </summary>
public class ConfigChangeAuditLog : IConfigChangeAuditLog
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private const int MaxCapacity = 200;

    private readonly ConcurrentQueue<ConfigChangeAudit> _entries = new();
    private int _totalCount;
    private long _evictedCount;
    private bool _loaded;

    private readonly ConcurrentQueue<PendingNotification> _pendingNotifications = new();
    private readonly IGatewayRepository _repository;
    private readonly ILogger<ConfigChangeAuditLog> _logger;

    /// <inheritdoc />
    public event Action<string, string, string?, object?>? OnConfigChanged;

    public ConfigChangeAuditLog(IGatewayRepository repository, ILogger<ConfigChangeAuditLog> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>Load persisted entries from repository on first access.</summary>
    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        lock (_entries)
        {
            if (_loaded) return;
            _loaded = true;
        }

        try
        {
            var persisted = await _repository.GetAuditLogsAsync(MaxCapacity);
            foreach (var entity in persisted)
            {
                _entries.Enqueue(entity.ToConfigChangeAudit());
                Interlocked.Increment(ref _totalCount);
            }
            _logger.LogDebug("Loaded {Count} audit entries from repository", persisted.Count);
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

        _ = Task.Run(async () =>
        {
            try { await EnsureLoadedAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Background audit log initialization failed"); }
        });
    }

    /// <inheritdoc />
    public void Record(ConfigChangeAudit entry)
    {
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

        _logger.LogDebug("Audit queued: {Action} on '{Target}' — {Status}",
            entry.Action, entry.Target,
            entry.Success ? "OK" : $"FAIL: {entry.ErrorMessage}");

        // Persist to repository (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try { await _repository.SaveAuditLogAsync(entry.ToEntity()); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to persist audit entry"); }
        });

        // Queue the notification for the background dispatcher to deliver.
        // This is reliable regardless of when consumers are constructed.
        if (entry.Success)
        {
            _pendingNotifications.Enqueue(new PendingNotification
            {
                EventType = entry.Action,
                Target = entry.Target,
                Operator = entry.Operator,
                Details = entry.After
            });
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
        EnsureLoaded();

        var entries = _entries.ToArray();
        if (entries.Length <= 1)
            return entries.Take(count).ToList();

        Array.Sort(entries, (a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return entries.Take(count).ToList();
    }

    /// <inheritdoc />
    public int Count => _entries.Count;

    /// <inheritdoc />
    public long EvictedCount => Volatile.Read(ref _evictedCount);

    /// <summary>Query audit logs by target from repository.</summary>
    public async Task<IReadOnlyList<ConfigChangeAudit>> GetByTargetAsync(string target, int count = 50)
    {
        var entities = await _repository.GetAuditLogsByTargetAsync(target, count);
        return entities.ToConfigChangeAudits();
    }

    /// <inheritdoc />
    public bool TryDequeuePendingNotification(out PendingNotification notification)
        => _pendingNotifications.TryDequeue(out notification);

    /// <inheritdoc />
    public void InvokeOnConfigChanged(string eventType, string target, string? @operator, object? details)
        => OnConfigChanged?.Invoke(eventType, target, @operator, details);
}
