using Aneiang.Yarp.Dashboard.Models.Dtos;
using Aneiang.Yarp.Services;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Services;

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
        var destinations = cluster.Destinations?.Select(d => new DashboardDestinationResponse
        {
            Name = d.Key,
            Address = d.Value.Address,
            Health = d.Value.Health,
            Host = d.Value.Host,
            Metadata = d.Value.Metadata?.Count > 0
                ? d.Value.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
                : null,
            ActiveHealth = "Unknown",
            PassiveHealth = "Unknown"
        }).ToList() ?? new List<DashboardDestinationResponse>();

        var isEditable = IsClusterEditable(cluster.ClusterId, dynamicConfig);

        return new DashboardClusterResponse
        {
            ClusterId = cluster.ClusterId,
            LoadBalancingPolicy = cluster.LoadBalancingPolicy ?? "Default",
            SessionAffinity = MapSessionAffinity(cluster.SessionAffinity),
            HealthCheck = MapHealthCheck(cluster.HealthCheck),
            HttpClient = MapHttpClient(cluster.HttpClient),
            HttpRequest = null,
            Metadata = cluster.Metadata?.Count > 0
                ? cluster.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
                : null,
            Destinations = destinations,
            HealthyCount = 0,
            UnknownCount = destinations.Count,
            UnhealthyCount = 0,
            TotalCount = destinations.Count,
            IsEditable = isEditable
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
    /// Determines if a cluster is editable based on its source.
    /// </summary>
    private static bool IsClusterEditable(string clusterId, DynamicYarpConfigService dynamicConfig)
    {
        var dynConfig = dynamicConfig.GetDynamicConfig();
        var dynCluster = dynConfig?.Clusters.FirstOrDefault(dc =>
            string.Equals(dc.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

        if (dynCluster != null)
        {
            return dynCluster.Source != "config";
        }

        return true;
    }
}
