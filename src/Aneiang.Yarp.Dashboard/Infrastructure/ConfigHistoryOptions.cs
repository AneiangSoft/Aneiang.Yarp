namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// Configuration history and snapshot options bound from <c>Gateway:Dashboard:ConfigHistory</c>.
/// </summary>
public sealed class ConfigHistoryOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Gateway:Dashboard:ConfigHistory";

    /// <summary>Whether low-risk mutations create automatic snapshots. Default: true.</summary>
    public bool AutoSnapshotBeforeMutation { get; set; } = true;

    /// <summary>Whether low-risk mutation snapshots are queued asynchronously. Default: true.</summary>
    public bool AsyncSnapshotForLowRiskMutation { get; set; } = true;

    /// <summary>Maximum number of configuration snapshots to retain. Default: 50.</summary>
    public int MaxSnapshots { get; set; } = 50;

    /// <summary>Maximum number of pending async snapshot jobs. Default: 256.</summary>
    public int SnapshotQueueCapacity { get; set; } = 256;
}
