using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Infrastructure.Health;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Application;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.Policy.Services;
using Aneiang.Yarp.Dashboard.Modules.Waf.Services;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Controllers;

/// <summary>
/// API controller for configuration knowledge and health scoring.
/// </summary>
[ApiController]
public class ConfigKnowledgeController : ControllerBase
{
    private readonly IConfigKnowledgeService _knowledgeService;
    private readonly ConfigHealthService _healthService;
    private readonly IDynamicYarpConfigService _dynamicConfig;
    private readonly IWafSettingsPersistenceService? _wafPersistence;
    private readonly IGatewayPolicyService? _policyService;
    private readonly IConfigPersistenceService _configPersistence;
    private readonly ILogger<ConfigKnowledgeController> _logger;

    public ConfigKnowledgeController(
        IConfigKnowledgeService knowledgeService,
        ConfigHealthService healthService,
        IDynamicYarpConfigService dynamicConfig,
        IConfigPersistenceService configPersistence,
        ILogger<ConfigKnowledgeController> logger,
        IWafSettingsPersistenceService? wafPersistence = null,
        IGatewayPolicyService? policyService = null)
    {
        _knowledgeService = knowledgeService;
        _healthService = healthService;
        _dynamicConfig = dynamicConfig;
        _configPersistence = configPersistence;
        _logger = logger;
        _wafPersistence = wafPersistence;
        _policyService = policyService;
    }

    /// <summary>Get all knowledge topics.</summary>
    [HttpGet("api/config/knowledge")]
    public async Task<IActionResult> GetAllTopics(CancellationToken ct)
    {
        var topics = await _knowledgeService.GetAllTopicsAsync(ct);
        return Ok(ApiResponse.Ok(topics.Select(t => new
        {
            t.TopicId,
            t.Title,
            t.Category,
            t.Summary,
            keyPoints = t.KeyPoints,
            t.DocUrl
        })));
    }

    /// <summary>Get a specific knowledge topic.</summary>
    [HttpGet("api/config/knowledge/{topicId}")]
    public async Task<IActionResult> GetTopic(string topicId, CancellationToken ct)
    {
        var topic = await _knowledgeService.GetTopicAsync(topicId, ct);
        if (topic == null)
            return NotFound(ApiResponse.Fail($"Topic '{topicId}' not found", 404));

        return Ok(ApiResponse.Ok(topic));
    }

    /// <summary>Search the knowledge base.</summary>
    [HttpGet("api/config/knowledge/search")]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(ApiResponse.Fail("Query parameter 'q' is required"));

        var result = await _knowledgeService.SearchAsync(q, ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>Get configuration health score.</summary>
    [HttpGet("api/config/health")]
    public async Task<IActionResult> GetHealth([FromQuery] bool forceRefresh = false, CancellationToken ct = default)
    {
        try
        {
            var context = await BuildHealthContextAsync(ct);
            var report = await _healthService.EvaluateAsync(context, forceRefresh, ct);

            return Ok(ApiResponse.Ok(new
            {
                report.Score,
                report.Grade,
                report.TotalRules,
                report.TriggeredRules,
                issues = report.Issues,
                evaluatedAt = report.EvaluatedAt
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConfigHealth] Failed to evaluate health");
            return StatusCode(500, ApiResponse.Fail("Failed to evaluate configuration health"));
        }
    }

    private async Task<ConfigHealthContext> BuildHealthContextAsync(CancellationToken ct)
    {
        var routes = _dynamicConfig.GetRoutes();
        var clusters = _dynamicConfig.GetClusters();
        var context = new ConfigHealthContext();

        // Map routes
        foreach (var route in routes)
        {
            var routeInfo = new RouteInfo
            {
                RouteId = route.RouteId,
                ClusterId = route.ClusterId,
                Order = route.Order,
                HasTransforms = route.Transforms != null && route.Transforms.Count > 0
            };

            if (route.Transforms != null)
            {
                foreach (var transform in route.Transforms)
                {
                    if (transform.ContainsKey("PathPattern"))
                        routeInfo.UsesPathPattern = true;
                    if (transform.ContainsKey("PathRemovePrefix"))
                        routeInfo.UsesPathRemovePrefix = true;
                }
            }

            if (route.Metadata != null && route.Metadata.TryGetValue("Retry:Enabled", out var retryVal))
            {
                if (bool.TryParse(retryVal, out var enabled) && enabled)
                    context.RoutesWithRetry.Add(route.RouteId);
            }

            context.Routes.Add(routeInfo);
        }

        // Map clusters
        foreach (var cluster in clusters)
        {
            var clusterInfo = new ClusterInfo
            {
                ClusterId = cluster.ClusterId,
                DestinationCount = cluster.Destinations?.Count ?? 0,
                LoadBalancingPolicy = cluster.LoadBalancingPolicy
            };

            if (cluster.HealthCheck != null)
            {
                clusterInfo.HasHealthCheck = true;
                clusterInfo.HasActiveHealthCheck = cluster.HealthCheck.Active?.Enabled ?? false;
            }

            if (cluster.HttpClient?.EnableMultipleHttp2Connections != null)
            {
                clusterInfo.EnableMultipleHttp2Connections = cluster.HttpClient.EnableMultipleHttp2Connections == true;
            }

            if (cluster.Metadata != null)
            {
                if (cluster.Metadata.TryGetValue("CircuitBreaker:Enabled", out var cbVal) &&
                    bool.TryParse(cbVal, out var cbEnabled) && cbEnabled)
                {
                    context.ClustersWithCircuitBreaker.Add(cluster.ClusterId);
                }
            }

            context.Clusters.Add(clusterInfo);
        }

        // Check policy service
        if (_policyService != null)
        {
            try
            {
                var clusterPolicies = await _policyService.GetAllClusterPoliciesAsync();
                foreach (var policy in clusterPolicies)
                {
                    if (policy.CircuitBreaker?.Enabled == true)
                    {
                        foreach (var clusterId in policy.AppliedClusters)
                            context.ClustersWithCircuitBreaker.Add(clusterId);
                    }
                }

                var routePolicies = await _policyService.GetAllRoutePoliciesAsync();
                foreach (var policy in routePolicies)
                {
                    if (policy.Retry?.Enabled == true)
                    {
                        foreach (var routeId in policy.AppliedRoutes)
                            context.RoutesWithRetry.Add(routeId);
                    }
                }
            }
            catch { /* Policy service may not be available */ }
        }

        // Get WAF settings
        if (_wafPersistence != null)
        {
            try
            {
                var wafData = await _wafPersistence.LoadAsync(ct);
                if (wafData != null)
                {
                    context.WafEnabled = wafData.Enabled;
                    context.WafIpBlacklistCount = wafData.IpBlacklist.Count;
                    context.WafMaxRequestBodySize = wafData.MaxRequestBodySize;
                }
            }
            catch { /* WAF settings may not be available */ }
        }

        // Get snapshot count
        try
        {
            var history = await _configPersistence.GetHistoryAsync();
            context.SnapshotCount = history?.Count ?? 0;
        }
        catch { /* Snapshot service may not be available */ }

        return context;
    }
}
