using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Resilience;

public interface IDestinationCandidateCoordinator
{
    ValueTask<IReadOnlyList<DestinationState>> ApplyAsync(HttpContext context, bool excludeAttempted, CancellationToken cancellationToken = default);
    void MarkAttempted(HttpContext context, DestinationState? destination);
}

public sealed class DestinationCandidateCoordinator(
    ICircuitStateStore circuitStateStore,
    GatewayPluginExecutionPlanProvider planProvider) : IDestinationCandidateCoordinator
{
    private static readonly object InitialCandidatesKey = new();
    private static readonly object AttemptedDestinationsKey = new();

    public ValueTask<IReadOnlyList<DestinationState>> ApplyAsync(
        HttpContext context,
        bool excludeAttempted,
        CancellationToken cancellationToken = default)
    {
        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        if (proxyFeature is null)
        {
            return ValueTask.FromResult<IReadOnlyList<DestinationState>>(Array.Empty<DestinationState>());
        }

        if (!context.Items.TryGetValue(InitialCandidatesKey, out var initialValue))
        {
            initialValue = proxyFeature.AvailableDestinations.ToArray();
            context.Items[InitialCandidatesKey] = initialValue;
        }

        var initialCandidates = (IReadOnlyList<DestinationState>)initialValue!;
        var attempted = GetAttempted(context);
        var clusterId = proxyFeature.Cluster.Config.ClusterId;
        planProvider.Current.CircuitBreakerByCluster.TryGetValue(clusterId, out var circuitConfig);
        var candidates = new List<DestinationState>(initialCandidates.Count);

        foreach (var destination in initialCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (excludeAttempted && attempted.Contains(destination.DestinationId))
            {
                continue;
            }

            if (circuitConfig is not { Enabled: true } ||
                circuitStateStore.TryAcquire(clusterId, destination.DestinationId, config: circuitConfig))
            {
                candidates.Add(destination);
            }
        }

        proxyFeature.AvailableDestinations = candidates;
        proxyFeature.ProxiedDestination = null;
        return ValueTask.FromResult<IReadOnlyList<DestinationState>>(candidates);
    }

    public void MarkAttempted(HttpContext context, DestinationState? destination)
    {
        if (destination is not null)
        {
            GetAttempted(context).Add(destination.DestinationId);
        }
    }

    private static HashSet<string> GetAttempted(HttpContext context)
    {
        if (context.Items.TryGetValue(AttemptedDestinationsKey, out var value))
        {
            return (HashSet<string>)value!;
        }

        var attempted = new HashSet<string>(StringComparer.Ordinal);
        context.Items[AttemptedDestinationsKey] = attempted;
        return attempted;
    }
}
