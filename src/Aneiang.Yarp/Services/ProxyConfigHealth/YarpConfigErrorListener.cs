using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services.ProxyConfigHealth;

/// <summary>
/// Captures YARP proxy configuration apply failures into <see cref="IProxyConfigErrorStore"/>.
/// YARP collects all <c>IConfigChangeListener</c> registrations via DI; registering this
/// singleton is enough to receive callbacks for every reload (initial + runtime).
/// </summary>
public sealed class YarpConfigErrorListener : IConfigChangeListener
{
    private static readonly Regex ClusterIdPattern =
        new(@"cluster\s*'([^']+)'", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RouteIdPattern =
        new(@"route\s*'([^']+)'", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IProxyConfigErrorStore _store;
    private readonly ILogger<YarpConfigErrorListener> _logger;

    public YarpConfigErrorListener(
        IProxyConfigErrorStore store,
        ILogger<YarpConfigErrorListener> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public void ConfigurationLoadingFailed(IProxyConfigProvider configProvider, Exception exception)
    {
        _logger.LogError(exception, "Proxy configuration loading failed.");
        Capture(exception, source: "reload");
    }

    /// <inheritdoc />
    public void ConfigurationLoaded(IReadOnlyList<IProxyConfig> proxyConfigs)
    {
        // Loaded but not yet applied — do not mark healthy until applied.
    }

    /// <inheritdoc />
    public void ConfigurationApplyingFailed(IReadOnlyList<IProxyConfig> proxyConfigs, Exception exception)
    {
        _logger.LogError(exception, "Proxy configuration applying failed.");
        Capture(exception, source: "reload");
    }

    /// <inheritdoc />
    public void ConfigurationApplied(IReadOnlyList<IProxyConfig> proxyConfigs)
    {
        _store.MarkHealthy();
    }

    private void Capture(Exception exception, string source)
    {
        // Unwind aggregate exceptions so each inner fault is recorded with its own attribution.
        var flat = Flatten(exception);
        if (flat.Count == 0)
        {
            flat.Add(exception);
        }

        var now = DateTime.UtcNow;
        var errors = new List<ProxyConfigError>(flat.Count);
        foreach (var ex in flat)
        {
            var (routeId, clusterId) = Attribute(ex);
            errors.Add(new ProxyConfigError
            {
                RouteId = routeId,
                ClusterId = clusterId,
                Message = ex.Message?.Trim() ?? ex.GetType().Name,
                ExceptionType = ex.GetType().FullName,
                OccurredAt = now,
                Source = source
            });
        }

        // Each reload outcome replaces the previous reload errors so the store
        // always reflects the latest apply attempt (not a growing history).
        _store.ReplaceErrors(source, errors);
    }

    private static List<Exception> Flatten(Exception exception)
    {
        var result = new List<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        FlattenCore(exception, result, visited);
        return result;
    }

    private static void FlattenCore(Exception exception, List<Exception> result, HashSet<Exception> visited)
    {
        if (exception == null || !visited.Add(exception)) return;

        if (exception is AggregateException agg)
        {
            foreach (var inner in agg.InnerExceptions)
            {
                FlattenCore(inner, result, visited);
            }
            // If the aggregate itself carries a distinct message, keep it as a fallback only
            // when there were no inner exceptions to record.
            if (result.Count == 0 && !string.IsNullOrWhiteSpace(agg.Message))
            {
                result.Add(agg);
            }
            return;
        }

        result.Add(exception);
        if (exception.InnerException != null)
        {
            FlattenCore(exception.InnerException, result, visited);
        }
    }

    private static (string? routeId, string? clusterId) Attribute(Exception exception)
    {
        // Walk inner chain so a wrapped validation message is still attributed.
        for (var ex = exception; ex != null; ex = ex.InnerException)
        {
            var msg = ex.Message;
            if (string.IsNullOrEmpty(msg)) continue;

            var clusterMatch = ClusterIdPattern.Match(msg);
            var routeMatch = RouteIdPattern.Match(msg);

            if (clusterMatch.Success || routeMatch.Success)
            {
                return (
                    routeMatch.Success ? routeMatch.Groups[1].Value : null,
                    clusterMatch.Success ? clusterMatch.Groups[1].Value : null);
            }
        }

        return (null, null);
    }
}
