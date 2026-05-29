using System.Collections.Concurrent;
using System.Text.Json;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Services.Implements;

/// <summary>
/// In-memory audit log store for gateway configuration changes.
/// Thread-safe, bounded by max capacity (ring-buffer style).
/// </summary>
public class ConfigChangeAuditLog : IConfigChangeAuditLog
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ConcurrentQueue<ConfigChangeAudit> _entries = new();
    private const int MaxCapacity = 200;
    private int _totalCount;
    private long _evictedCount;

    private readonly ILogger<ConfigChangeAuditLog> _logger;

    /// <summary>
    /// Raised when a config change audit entry is recorded (success only).
    /// </summary>
    public event Action<string, string, string?, object?>? OnConfigChanged;

    public ConfigChangeAuditLog(ILogger<ConfigChangeAuditLog> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Record(ConfigChangeAudit entry)
    {
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
            else
            {
                break;
            }
        }

        _logger.LogDebug("Audit: {Action} on '{Target}' by {Operator} - {Status}",
            entry.Action, entry.Target, entry.Operator ?? "unknown",
            entry.Success ? "OK" : $"FAIL: {entry.ErrorMessage}");

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
        return _entries.OrderByDescending(e => e.Timestamp).Take(count).ToList();
    }

    /// <inheritdoc />
    public int Count => _entries.Count;

    /// <inheritdoc />
    public long EvictedCount => Volatile.Read(ref _evictedCount);
}
