using System.Text.Json;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Plugins;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Plugin.ServiceDiscovery;

/// <summary>Refreshes cluster destinations for enabled service-discovery bindings.</summary>
public sealed class ServiceDiscoveryRefreshService : IPluginRuntimeResource
{
    public const string PluginId = "service-discovery";

    private readonly GatewayPluginExecutionPlanProvider _executionPlans;
    private readonly AneiangProxyConfigProvider _configProvider;
    private readonly IGatewaySnapshotCompiler _snapshotCompiler;
    private readonly IGatewaySnapshotPublisher _snapshotPublisher;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ServiceDiscoveryRefreshService> _logger;
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _nextRefresh = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _stoppedAt;
    private Exception? _lastError;
    private long _refreshes;
    private long _publishedSnapshots;
    private long _httpRequests;

    string IPluginRuntimeResource.PluginId => PluginId;
    public string ResourceId => "service-discovery:endpoint-refresh";
    public string ResourceType => "endpoint-refresh";

    public ServiceDiscoveryRefreshService(
        GatewayPluginExecutionPlanProvider executionPlans,
        AneiangProxyConfigProvider configProvider,
        IGatewaySnapshotCompiler snapshotCompiler,
        IGatewaySnapshotPublisher snapshotPublisher,
        IHttpClientFactory httpClientFactory,
        ILogger<ServiceDiscoveryRefreshService> logger)
    {
        _executionPlans = executionPlans;
        _configProvider = configProvider;
        _snapshotCompiler = snapshotCompiler;
        _snapshotPublisher = snapshotPublisher;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public ValueTask StartResourceAsync(CancellationToken cancellationToken)
    {
        if (_runTask is { IsCompleted: false }) return ValueTask.CompletedTask;
        _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _startedAt = DateTimeOffset.UtcNow;
        _stoppedAt = null;
        _lastError = null;
        _runTask = RunAsync(_runCancellation.Token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopResourceAsync(CancellationToken cancellationToken)
    {
        var cancellation = Interlocked.Exchange(ref _runCancellation, null);
        var task = Interlocked.Exchange(ref _runTask, null);
        if (cancellation == null) return;
        await cancellation.CancelAsync().ConfigureAwait(false);
        if (task != null)
        {
            try { await task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        cancellation.Dispose();
        _nextRefresh.Clear();
        _stoppedAt = DateTimeOffset.UtcNow;
    }

    public ValueTask<PluginRuntimeResourceSnapshot> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var running = _runTask is { IsCompleted: false };
        var health = _lastError != null ? PluginResourceHealthStatus.Degraded :
            running ? PluginResourceHealthStatus.Healthy : PluginResourceHealthStatus.Stopped;
        return ValueTask.FromResult(new PluginRuntimeResourceSnapshot(
            ResourceId, ResourceType, running, health, _startedAt, _stoppedAt, _lastError?.Message,
            new Dictionary<string, long>
            {
                ["refreshes"] = Interlocked.Read(ref _refreshes),
                ["publishedSnapshots"] = Interlocked.Read(ref _publishedSnapshots),
                ["httpRequests"] = Interlocked.Read(ref _httpRequests),
                ["memoryBytes"] = _nextRefresh.Count * 512L
            }));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshDueBindingsAsync(cancellationToken).ConfigureAwait(false);
                _lastError = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _lastError = exception;
                _logger.LogError(exception, "Service discovery refresh cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshDueBindingsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var bindings = _executionPlans.Current.ServiceDiscoveryByCluster
            .Where(pair => pair.Value.Enabled)
            .ToArray();
        var activeClusters = bindings.Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _nextRefresh.Keys.Where(clusterId => !activeClusters.Contains(clusterId)).ToArray())
            _nextRefresh.Remove(stale);

        foreach (var (clusterId, config) in bindings)
        {
            if (_nextRefresh.TryGetValue(clusterId, out var dueAt) && dueAt > now) continue;
            _nextRefresh[clusterId] = now.AddSeconds(Math.Clamp(config.RefreshSeconds, 5, 3600));

            try
            {
                var endpoints = await ResolveEndpointsAsync(config, cancellationToken).ConfigureAwait(false);
                await PublishDestinationsAsync(clusterId, endpoints, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _refreshes);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _lastError = exception;
                _logger.LogWarning(exception, "Failed to refresh service discovery binding for cluster {ClusterId}", clusterId);
            }
        }
    }

    private async Task<IReadOnlyList<string>> ResolveEndpointsAsync(ServiceDiscoveryExecutionConfig config, CancellationToken cancellationToken)
    {
        if (string.Equals(config.Mode, "Static", StringComparison.OrdinalIgnoreCase))
            return NormalizeEndpoints(config.StaticEndpoints);
        if (!Uri.TryCreate(BuildProviderUri(config), UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"{config.Mode} service discovery requires an absolute HTTP or HTTPS endpoint.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.RequestTimeoutSeconds, 1, 60)));
        Interlocked.Increment(ref _httpRequests);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (string.Equals(config.Mode, "Kubernetes", StringComparison.OrdinalIgnoreCase))
        {
            var tokenPath = "/var/run/secrets/kubernetes.io/serviceaccount/token";
            if (File.Exists(tokenPath)) request.Headers.Authorization = new("Bearer", await File.ReadAllTextAsync(tokenPath, timeout.Token));
        }
        using var response = await _httpClientFactory.CreateClient("service-discovery")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 32 }, timeout.Token).ConfigureAwait(false);
        return NormalizeEndpoints(ReadProviderEndpoints(config, document.RootElement));
    }

    private static string BuildProviderUri(ServiceDiscoveryExecutionConfig config)
    {
        if (string.Equals(config.Mode, "HttpJson", StringComparison.OrdinalIgnoreCase)) return config.Endpoint ?? "";
        if (string.IsNullOrWhiteSpace(config.Endpoint) || string.IsNullOrWhiteSpace(config.ServiceName))
            throw new InvalidOperationException($"{config.Mode} requires endpoint and serviceName.");
        var root = config.Endpoint.TrimEnd('/');
        var service = Uri.EscapeDataString(config.ServiceName);
        return config.Mode.ToUpperInvariant() switch
        {
            "CONSUL" => $"{root}/v1/health/service/{service}?passing=true",
            "NACOS" => $"{root}/nacos/v1/ns/instance/list?serviceName={service}&healthyOnly=true",
            "EUREKA" => $"{root}/eureka/apps/{service}",
            "KUBERNETES" => $"{root}/api/v1/namespaces/{Uri.EscapeDataString(config.Namespace)}/endpoints/{service}",
            _ => throw new InvalidOperationException($"Unsupported service discovery mode '{config.Mode}'.")
        };
    }

    private static IEnumerable<string> ReadProviderEndpoints(ServiceDiscoveryExecutionConfig config, JsonElement root)
    {
        var scheme = config.Scheme is "https" ? "https" : "http";
        if (string.Equals(config.Mode, "HttpJson", StringComparison.OrdinalIgnoreCase)) return ReadEndpointValues(root);
        var results = new List<string>();
        if (string.Equals(config.Mode, "Consul", StringComparison.OrdinalIgnoreCase) && root.ValueKind == JsonValueKind.Array)
            foreach (var item in root.EnumerateArray()) AddHostPort(results, scheme, GetString(item, "Service", "Address") ?? GetString(item, "Node", "Address"), GetInt(item, "Service", "Port"));
        else if (string.Equals(config.Mode, "Nacos", StringComparison.OrdinalIgnoreCase) && root.TryGetProperty("hosts", out var hosts) && hosts.ValueKind == JsonValueKind.Array)
            foreach (var item in hosts.EnumerateArray()) if (!item.TryGetProperty("healthy", out var healthy) || healthy.GetBoolean()) AddHostPort(results, scheme, GetString(item, "ip"), GetInt(item, "port"));
        else if (string.Equals(config.Mode, "Eureka", StringComparison.OrdinalIgnoreCase) && TryPath(root, out var instances, "application", "instance"))
        {
            if (instances.ValueKind == JsonValueKind.Array)
                foreach (var instance in instances.EnumerateArray()) AddHostPort(results, scheme, GetString(instance, "ipAddr") ?? GetString(instance, "hostName"), GetInt(instance, "port", "$"));
            else
                AddHostPort(results, scheme, GetString(instances, "ipAddr") ?? GetString(instances, "hostName"), GetInt(instances, "port", "$"));
        }
        else if (string.Equals(config.Mode, "Kubernetes", StringComparison.OrdinalIgnoreCase) && root.TryGetProperty("subsets", out var subsets) && subsets.ValueKind == JsonValueKind.Array)
            foreach (var subset in subsets.EnumerateArray()) if (subset.TryGetProperty("addresses", out var addresses) && subset.TryGetProperty("ports", out var ports)) foreach (var address in addresses.EnumerateArray()) foreach (var port in ports.EnumerateArray()) AddHostPort(results, scheme, GetString(address, "ip"), GetInt(port, "port"));
        return results;
    }

    private static void AddHostPort(List<string> results, string scheme, string? host, int? port)
    { if (!string.IsNullOrWhiteSpace(host) && port is > 0 and <= 65535) results.Add($"{scheme}://{host}:{port}"); }
    private static string? GetString(JsonElement element, params string[] path) => TryPath(element, out var value, path) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? GetInt(JsonElement element, params string[] path) => TryPath(element, out var value, path) && (value.ValueKind == JsonValueKind.Number ? value.TryGetInt32(out var number) : int.TryParse(value.GetString(), out number)) ? number : null;
    private static bool TryPath(JsonElement element, out JsonElement value, params string[] path)
    { value = element; foreach (var part in path) { if (part == "$" && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$", out var wrapped)) { value = wrapped; continue; } if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value)) return false; } return true; }

    private async Task PublishDestinationsAsync(string clusterId, IReadOnlyList<string> endpoints, CancellationToken cancellationToken)
    {
        await _publishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var routes = _configProvider.GetRoutes();
            var clusters = _configProvider.GetClusters();
            var index = clusters.Select((cluster, position) => (cluster, position))
                .FirstOrDefault(item => string.Equals(item.cluster.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (index.cluster == null) return;

            var destinations = endpoints.Select((address, position) => new KeyValuePair<string, DestinationConfig>(
                    $"discovery-{position:D4}", new DestinationConfig { Address = address }))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (DestinationsEqual(index.cluster.Destinations, destinations)) return;

            var updatedClusters = clusters.ToArray();
            updatedClusters[index.position] = index.cluster with { Destinations = destinations };
            var version = _snapshotPublisher.Current.Version + 1;
            var snapshot = await _snapshotCompiler.CompileAsync(routes, updatedClusters, version, cancellationToken).ConfigureAwait(false);
            _snapshotPublisher.Publish(snapshot);
            _configProvider.Update(snapshot.Routes, snapshot.Clusters);
            Interlocked.Increment(ref _publishedSnapshots);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private static IEnumerable<string> ReadEndpointValues(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("endpoints", out var endpoints)) root = endpoints;
        if (root.ValueKind != JsonValueKind.Array)
            throw new JsonException("Service discovery response must be an array or an object containing an endpoints array.");

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (value != null) yield return value;
            }
            else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("address", out var address) && address.ValueKind == JsonValueKind.String)
            {
                var value = address.GetString();
                if (value != null) yield return value;
            }
        }
    }

    private static IReadOnlyList<string> NormalizeEndpoints(IEnumerable<string> endpoints) => endpoints
        .Select(value => value?.Trim())
        .Where(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        .Select(value => value!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool DestinationsEqual(
        IReadOnlyDictionary<string, DestinationConfig>? current,
        IReadOnlyDictionary<string, DestinationConfig> next)
    {
        if (current == null || current.Count != next.Count) return false;
        return current.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Key, pair.Value.Address))
            .SequenceEqual(next.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => (pair.Key, pair.Value.Address)));
    }
}
