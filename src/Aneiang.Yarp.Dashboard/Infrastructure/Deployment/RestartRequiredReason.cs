namespace Aneiang.Yarp.Dashboard.Infrastructure.Deployment;

/// <summary>
/// Represents a reason why a restart is required for a configuration change to take effect.
/// </summary>
public sealed class RestartRequiredReason
{
    /// <summary>Unique key identifying this restart reason.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Title describing the restart reason.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Detailed message about the configuration change.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Configuration path that triggered the restart requirement.</summary>
    public string ConfigPath { get; init; } = string.Empty;

    /// <summary>Timestamp when the restart reason was detected.</summary>
    public DateTime DetectedAt { get; init; }
}
