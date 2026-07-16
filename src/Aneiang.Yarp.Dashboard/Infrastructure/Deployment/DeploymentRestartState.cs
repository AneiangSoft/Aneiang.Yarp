namespace Aneiang.Yarp.Dashboard.Infrastructure.Deployment;

/// <summary>
/// Tracks runtime configuration changes that require a process restart to take effect.
/// </summary>
public sealed class DeploymentRestartState
{
    private readonly object _lock = new();
    private readonly Dictionary<string, RestartRequiredReason> _reasons = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRestartRequired
    {
        get
        {
            lock (_lock) return _reasons.Count > 0;
        }
    }

    public IReadOnlyList<RestartRequiredReason> GetReasons()
    {
        lock (_lock)
        {
            return _reasons.Values
                .OrderByDescending(r => r.DetectedAt)
                .ToList()
                .AsReadOnly();
        }
    }

    public void MarkRestartRequired(string key, string title, string message, string configPath)
    {
        lock (_lock)
        {
            _reasons[key] = new RestartRequiredReason
            {
                Key = key,
                Title = title,
                Message = message,
                ConfigPath = configPath,
                DetectedAt = DateTime.Now
            };
        }
    }
}

