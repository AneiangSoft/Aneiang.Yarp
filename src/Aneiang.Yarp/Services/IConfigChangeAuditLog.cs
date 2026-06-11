using Aneiang.Yarp.Models;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Interface for recording gateway configuration change audit entries.
/// Inherits <see cref="IConfigChangeNotifier"/> for event-based notification.
/// </summary>
public interface IConfigChangeAuditLog : IConfigChangeNotifier
{
    /// <summary>Record a configuration change audit entry.</summary>
    void Record(ConfigChangeAudit entry);

    /// <summary>Record a successful configuration change.</summary>
    void RecordSuccess(string action, string target, string? operatorName = null, string? clientIp = null,
        object? before = null, object? after = null);

    /// <summary>Record a failed configuration change.</summary>
    void RecordFailure(string action, string target, string errorMessage, string? operatorName = null);

    /// <summary>Get recent audit entries (newest first).</summary>
    IReadOnlyList<ConfigChangeAudit> GetRecent(int count = 50);

    /// <summary>Get total entries currently stored.</summary>
    int Count { get; }

    /// <summary>Get total evicted entries.</summary>
    long EvictedCount { get; }
}
