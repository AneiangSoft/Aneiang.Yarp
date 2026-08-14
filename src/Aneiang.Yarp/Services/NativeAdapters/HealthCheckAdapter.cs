using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Cluster health check adapter: compiles to YARP native HealthCheck field.</summary>
public static class HealthCheckAdapter
{
    public const string PluginId = "native.cluster.health-check";

    public static NativePluginAdapterDescriptor Descriptor { get; } = new(PluginId, "Cluster Health Check", PluginBindingScope.Cluster);

    public static ClusterConfig Apply(ClusterConfig cluster, ClusterHealthCheckConfig value) =>
        cluster with { HealthCheck = ToHealthCheck(value) };

    private static HealthCheckConfig ToHealthCheck(ClusterHealthCheckConfig value)
    {
        if (value.Active == null && value.Passive == null && string.IsNullOrWhiteSpace(value.AvailableDestinationsPolicy))
            throw new ArgumentException("At least one health-check setting is required.");
        if (value.Active?.Interval <= TimeSpan.Zero || value.Active?.Timeout <= TimeSpan.Zero || value.Passive?.ReactivationPeriod <= TimeSpan.Zero)
            throw new ArgumentException("Health-check durations must be greater than zero.");
        return new HealthCheckConfig
        {
            Active = value.Active == null ? null : new ActiveHealthCheckConfig
            {
                Enabled = value.Active.Enabled,
                Interval = value.Active.Interval,
                Timeout = value.Active.Timeout,
                Policy = value.Active.Policy,
                Path = value.Active.Path,
                Query = value.Active.Query
            },
            Passive = value.Passive == null ? null : new PassiveHealthCheckConfig
            {
                Enabled = value.Passive.Enabled,
                Policy = value.Passive.Policy,
                ReactivationPeriod = value.Passive.ReactivationPeriod
            },
            AvailableDestinationsPolicy = value.AvailableDestinationsPolicy
        };
    }
}

/// <summary>Configuration model for <see cref="HealthCheckAdapter"/>.</summary>
public sealed class ClusterHealthCheckConfig
{
    public NativeActiveHealthCheckConfig? Active { get; init; }
    public NativePassiveHealthCheckConfig? Passive { get; init; }
    public string? AvailableDestinationsPolicy { get; init; }
}

public sealed class NativeActiveHealthCheckConfig
{
    public bool Enabled { get; init; }
    public TimeSpan? Interval { get; init; }
    public TimeSpan? Timeout { get; init; }
    public string? Policy { get; init; }
    public string? Path { get; init; }
    public string? Query { get; init; }
}

public sealed class NativePassiveHealthCheckConfig
{
    public bool Enabled { get; init; }
    public string? Policy { get; init; }
    public TimeSpan? ReactivationPeriod { get; init; }
}
