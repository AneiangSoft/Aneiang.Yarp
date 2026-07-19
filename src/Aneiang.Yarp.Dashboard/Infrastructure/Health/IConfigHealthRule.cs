namespace Aneiang.Yarp.Dashboard.Infrastructure.Health;

/// <summary>
/// Interface for a single configuration health check rule.
/// Rules are evaluated by ConfigHealthService to produce a health score.
/// </summary>
public interface IConfigHealthRule
{
    /// <summary>Unique rule identifier, e.g. "SEC001".</summary>
    string Id { get; }

    /// <summary>Category: Security, Reliability, Performance, BestPractice.</summary>
    string Category { get; }

    /// <summary>Severity level.</summary>
    Severity Level { get; }

    /// <summary>Human-readable title.</summary>
    string Title { get; }

    /// <summary>Description of what the rule checks.</summary>
    string Description { get; }

    /// <summary>Recommendation for fixing the issue.</summary>
    string Recommendation { get; }

    /// <summary>URL of the configuration page to fix this issue.</summary>
    string ConfigPageUrl { get; }

    /// <summary>Evaluate the rule against the current configuration.</summary>
    Task<HealthRuleResult> EvaluateAsync(ConfigHealthContext context, CancellationToken ct = default);
}

/// <summary>Severity levels for health rules.</summary>
public enum Severity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

/// <summary>Result of a single rule evaluation.</summary>
public class HealthRuleResult
{
    /// <summary>Rule ID.</summary>
    public string RuleId { get; set; } = "";

    /// <summary>Whether the rule was triggered (issue found).</summary>
    public bool Triggered { get; set; }

    /// <summary>Whether the rule is applicable to the current config.</summary>
    public bool IsApplicable { get; set; } = true;

    /// <summary>Human-readable details about the triggered issue.</summary>
    public string Detail { get; set; } = "";

    /// <summary>Category from the rule.</summary>
    public string Category { get; set; } = "";

    /// <summary>Severity from the rule.</summary>
    public Severity Level { get; set; }

    /// <summary>Title from the rule.</summary>
    public string Title { get; set; } = "";

    /// <summary>Recommendation from the rule.</summary>
    public string Recommendation { get; set; } = "";

    /// <summary>Config page URL from the rule.</summary>
    public string ConfigPageUrl { get; set; } = "";
}

/// <summary>Context passed to rules containing current configuration state.</summary>
public class ConfigHealthContext
{
    /// <summary>All configured routes (RouteId -> cluster binding).</summary>
    public List<RouteInfo> Routes { get; set; } = [];

    /// <summary>All configured clusters.</summary>
    public List<ClusterInfo> Clusters { get; set; } = [];

    /// <summary>WAF enabled status.</summary>
    public bool WafEnabled { get; set; }

    /// <summary>WAF IP blacklist count.</summary>
    public int WafIpBlacklistCount { get; set; }

    /// <summary>WAF max request body size.</summary>
    public long WafMaxRequestBodySize { get; set; }

    /// <summary>Circuit breaker policy count.</summary>
    public int CircuitBreakerPolicyCount { get; set; }

    /// <summary>Retry policy count.</summary>
    public int RetryPolicyCount { get; set; }

    /// <summary>Config snapshot count.</summary>
    public int SnapshotCount { get; set; }

    /// <summary>Cluster IDs that have circuit breaker applied.</summary>
    public HashSet<string> ClustersWithCircuitBreaker { get; set; } = [];

    /// <summary>Route IDs that have retry applied.</summary>
    public HashSet<string> RoutesWithRetry { get; set; } = [];
}

/// <summary>Simplified route info for health evaluation.</summary>
public class RouteInfo
{
    public string RouteId { get; set; } = "";
    public string? ClusterId { get; set; }
    public int? Order { get; set; }
    public bool HasTransforms { get; set; }
    public bool UsesPathPattern { get; set; }
    public bool UsesPathRemovePrefix { get; set; }
}

/// <summary>Simplified cluster info for health evaluation.</summary>
public class ClusterInfo
{
    public string ClusterId { get; set; } = "";
    public int DestinationCount { get; set; }
    public string? LoadBalancingPolicy { get; set; }
    public bool HasHealthCheck { get; set; }
    public bool HasActiveHealthCheck { get; set; }
    public bool EnableMultipleHttp2Connections { get; set; }
}
