using Aneiang.Yarp.Models;

namespace Aneiang.Yarp.Services;

public interface IConfigChangeAuditLog : IConfigChangeNotifier
{
    void Record(ConfigChangeAudit entry);
    void RecordSuccess(string action, string target, string? operatorName = null, string? clientIp = null,
        object? before = null, object? after = null);
    void RecordFailure(string action, string target, string errorMessage, string? operatorName = null);
    IReadOnlyList<ConfigChangeAudit> GetRecent(int count = 50);
    (IReadOnlyList<ConfigChangeAudit> Entries, int Total) GetPage(int page = 1, int pageSize = 50, string? action = null);
    int Count { get; }
    long EvictedCount { get; }
    bool TryDequeuePendingNotification(out PendingNotification notification);
    void InvokeOnConfigChanged(string eventType, string target, string? @operator, object? details);
}
