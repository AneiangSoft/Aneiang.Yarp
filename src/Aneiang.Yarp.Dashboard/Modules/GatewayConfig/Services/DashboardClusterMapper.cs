using Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;
using Aneiang.Yarp.Services;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Maps cluster configuration to dashboard response DTO.
/// </summary>
internal static class DashboardClusterMapper
{
    /// <summary>
    /// Maps a cluster configuration to dashboard response.
    /// </summary>
    public static DashboardClusterResponse MapToResponse(
        ClusterConfig cluster,
        DynamicYarpConfigService dynamicConfig)
    {
        var activeHealthConfigured = cluster.HealthCheck?.Active?.Enabled == true;
        var passiveHealthConfigured = cluster.HealthCheck?.Passive?.Enabled == true;

        var destinations = cluster.Destinations?.Select(d => new DashboardDestinationResponse
        {
            Name = d.Key,
            Address = d.Value.Address,
            Health = d.Value.Health,
            Host = d.Value.Host,
            Metadata = d.Value.Metadata?.Count > 0
                ? d.Value.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
                : null,
            ActiveHealth = ResolveHealthStatus(d.Value.Health, activeHealthConfigured),
            PassiveHealth = ResolveHealthStatus(d.Value.Health, passiveHealthConfigured)
        }).ToList() ?? new List<DashboardDestinationResponse>();

        var healthyCount = destinations.Count(d => string.Equals(d.Health, "Healthy", StringComparison.OrdinalIgnoreCase));
        var unhealthyCount = destinations.Count(d => string.Equals(d.Health, "Unhealthy", StringComparison.OrdinalIgnoreCase));
        var unknownCount = destinations.Count - healthyCount - unhealthyCount;

        var (isEditable, source) = GetClusterEditability(cluster.ClusterId, dynamicConfig);
        var dynamicCluster = dynamicConfig.GetDynamicConfig()?.Clusters.FirstOrDefault(c =>
            string.Equals(c.ClusterId, cluster.ClusterId, StringComparison.OrdinalIgnoreCase));

        return new DashboardClusterResponse
        {
            ClusterId = cluster.ClusterId,
            ClusterUid = dynamicCluster?.ClusterUid,
            ClusterKey = cluster.ClusterId ?? string.Empty,
            DisplayName = dynamicCluster?.DisplayName ?? cluster.ClusterId ?? string.Empty,
            LoadBalancingPolicy = cluster.LoadBalancingPolicy ?? "Default",
            SessionAffinity = MapSessionAffinity(cluster.SessionAffinity),
            HealthCheck = MapHealthCheck(cluster.HealthCheck),
            HttpClient = MapHttpClient(cluster.HttpClient),
            HttpRequest = MapHttpRequest(cluster.HttpRequest),
            Metadata = cluster.Metadata?.Count > 0
                ? cluster.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
                : null,
            Destinations = destinations,
            HealthyCount = healthyCount,
            UnknownCount = unknownCount,
            UnhealthyCount = unhealthyCount,
            TotalCount = destinations.Count,
            Source = source,
            IsEditable = isEditable,
            CircuitBreaker = MapCircuitBreaker(cluster.ClusterId ?? string.Empty, dynamicConfig)
        };
    }

    /// <summary>
    /// Maps session affinity configuration.
    /// </summary>
    private static SessionAffinityInfo? MapSessionAffinity(SessionAffinityConfig? config)
    {
        if (config == null)
            return null;

        return new SessionAffinityInfo
        {
            Enabled = config.Enabled ?? false,
            Policy = config.Policy,
            FailurePolicy = config.FailurePolicy,
            AffinityKeyName = config.AffinityKeyName,
            Cookie = config.Cookie != null ? new SessionAffinityCookieInfo
            {
                Domain = config.Cookie.Domain,
                Path = config.Cookie.Path,
                Expiration = config.Cookie.Expiration?.ToString(),
                MaxAge = config.Cookie.MaxAge?.ToString(),
                SecurePolicy = config.Cookie.SecurePolicy?.ToString(),
                HttpOnly = config.Cookie.HttpOnly ?? false,
                SameSite = config.Cookie.SameSite?.ToString(),
                IsEssential = config.Cookie.IsEssential ?? false
            } : null
        };
    }

    /// <summary>
    /// Maps health check configuration.
    /// </summary>
    private static HealthCheckInfo? MapHealthCheck(HealthCheckConfig? config)
    {
        if (config == null)
            return null;

        return new HealthCheckInfo
        {
            Active = config.Active != null ? new ActiveHealthCheckInfo
            {
                Enabled = config.Active.Enabled ?? false,
                Interval = config.Active.Interval?.ToString(),
                Timeout = config.Active.Timeout?.ToString(),
                Policy = config.Active.Policy,
                Path = config.Active.Path,
                Query = config.Active.Query
            } : null,
            Passive = config.Passive != null ? new PassiveHealthCheckInfo
            {
                Enabled = config.Passive.Enabled ?? false,
                Policy = config.Passive.Policy,
                ReactivationPeriod = config.Passive.ReactivationPeriod?.ToString()
            } : null,
            AvailableDestinationsPolicy = config.AvailableDestinationsPolicy
        };
    }

    /// <summary>
    /// Maps HTTP client configuration.
    /// </summary>
    private static HttpClientInfo? MapHttpClient(HttpClientConfig? config)
    {
        if (config == null)
            return null;

        return new HttpClientInfo
        {
            SslProtocols = config.SslProtocols?.ToString(),
            DangerousAcceptAnyServerCertificate = config.DangerousAcceptAnyServerCertificate ?? false,
            MaxConnectionsPerServer = config.MaxConnectionsPerServer,
            EnableMultipleHttp2Connections = config.EnableMultipleHttp2Connections ?? false,
            RequestHeaderEncoding = config.RequestHeaderEncoding,
            ResponseHeaderEncoding = config.ResponseHeaderEncoding,
            WebProxy = config.WebProxy != null ? new WebProxyInfo
            {
                Address = config.WebProxy.Address?.ToString(),
                BypassOnLocal = config.WebProxy.BypassOnLocal ?? false,
                UseDefaultCredentials = config.WebProxy.UseDefaultCredentials ?? false
            } : null
        };
    }

    /// <summary>
    /// Maps HTTP request configuration.
    /// </summary>
    private static HttpRequestInfo? MapHttpRequest(global::Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig? config)
    {
        if (config == null)
            return null;

        return new HttpRequestInfo
        {
            ActivityTimeout = config.ActivityTimeout?.ToString(),
            Version = config.Version?.ToString(),
            VersionPolicy = config.VersionPolicy?.ToString(),
            AllowResponseBuffering = config.AllowResponseBuffering
        };
    }

    /// <summary>
    /// Determines editability and source for a cluster.
    /// All clusters are editable. Source is preserved for display purposes only.
    /// </summary>
    private static (bool isEditable, string source) GetClusterEditability(string clusterId, DynamicYarpConfigService dynamicConfig)
    {
        var dynConfig = dynamicConfig.GetDynamicConfig();
        var dynCluster = dynConfig?.Clusters.FirstOrDefault(dc =>
            string.Equals(dc.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

        if (dynCluster != null)
        {
            return (true, dynCluster.Source);
        }

        return (true, "config"); // Not in dynamic config means it's from static config
    }

    /// <summary>
    /// Maps circuit breaker configuration from dynamic config.
    /// </summary>
    private static CircuitBreakerInfo? MapCircuitBreaker(string clusterId, DynamicYarpConfigService dynamicConfig)
    {
        var dynConfig = dynamicConfig.GetDynamicConfig();
        var dynCluster = dynConfig?.Clusters.FirstOrDefault(dc =>
            string.Equals(dc.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

        var cb = dynCluster?.CircuitBreaker;
        if (cb == null)
            return null;

        return new CircuitBreakerInfo
        {
            Enabled = cb.Enabled,
            FailureThreshold = cb.FailureThreshold,
            RecoveryTimeoutSeconds = cb.RecoveryTimeoutSeconds,
            HalfOpenMaxAttempts = cb.HalfOpenMaxAttempts,
            FailureStatusCodes = cb.FailureStatusCodes
        };
    }

    /// <summary>
    /// Resolves health status string based on YARP runtime health data.
    /// If the health check type is not configured, returns "Unknown" (not monitored).
    /// Otherwise returns the actual health status ("Healthy"/"Unhealthy") or "Unknown" if no data yet.
    /// </summary>
    private static string ResolveHealthStatus(string? yarpHealth, bool healthCheckConfigured)
    {
        if (!healthCheckConfigured)
            return "Unknown";

        return !string.IsNullOrWhiteSpace(yarpHealth) ? yarpHealth : "Unknown";
    }
}
