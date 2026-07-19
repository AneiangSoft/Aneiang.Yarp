using Aneiang.Yarp.Dashboard.Infrastructure.Health;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Tools;

/// <summary>
/// Partial class: Configuration knowledge and health check tool implementations.
/// </summary>
public partial class GatewayToolExecutor
{
    // ───────────── search_config_docs ─────────────

    private async Task<object> ExecuteSearchConfigDocsAsync(ToolArgs args, CancellationToken ct)
    {
        if (_configKnowledge == null)
            return new { error = "Configuration knowledge service is not available." };

        var query = args.Get("query");
        if (string.IsNullOrWhiteSpace(query))
            return new { error = "Query parameter is required." };

        var result = await _configKnowledge.SearchAsync(query, ct);
        if (result == null || result.Results.Count == 0)
            return new { query, message = "No results found.", results = Array.Empty<object>() };

        return new
        {
            query = result.Query,
            count = result.Results.Count,
            results = result.Results.Select(r => new
            {
                r.TopicId,
                r.Title,
                r.Summary,
                r.RelevanceScore,
                r.Snippet
            })
        };
    }

    // ───────────── get_feature_guide ─────────────

    private async Task<object> ExecuteGetFeatureGuideAsync(ToolArgs args, CancellationToken ct)
    {
        if (_configKnowledge == null)
            return new { error = "Configuration knowledge service is not available." };

        var topicId = args.Get("topic_id");
        if (string.IsNullOrWhiteSpace(topicId))
            return new { error = "topic_id parameter is required." };

        var entry = await _configKnowledge.GetTopicAsync(topicId, ct);
        if (entry == null)
            return new { error = $"Topic '{topicId}' not found. Available topics: load-balancing, health-check, circuit-breaker, request-retry, rate-limiting, waf, transforms, session-affinity, routing, http-client." };

        return new
        {
            entry.TopicId,
            entry.Title,
            entry.Category,
            entry.Summary,
            entry.Content,
            keyPoints = entry.KeyPoints,
            bestPractices = entry.BestPractices,
            commonMistakes = entry.CommonMistakes,
            entry.DocUrl,
            entry.ExampleConfig
        };
    }

    // ───────────── check_config_health ─────────────

    private async Task<object> ExecuteCheckConfigHealthAsync(ToolArgs args, CancellationToken ct)
    {
        if (_configHealthService == null)
            return new { error = "Config health service is not available." };

        var forceRefresh = args.GetBool("force_refresh");
        var context = await BuildHealthContextAsync(ct);
        var report = await _configHealthService.EvaluateAsync(context, forceRefresh, ct);

        return new
        {
            score = report.Score,
            grade = report.Grade,
            totalRules = report.TotalRules,
            triggeredRules = report.TriggeredRules,
            issues = report.Issues.Select(i => new
            {
                i.RuleId,
                i.Category,
                i.Level,
                i.Title,
                i.Description,
                i.Recommendation,
                i.ConfigPageUrl
            }),
            evaluatedAt = report.EvaluatedAt
        };
    }

    // ───────────── suggest_configuration ─────────────

    private async Task<object> ExecuteSuggestConfigurationAsync(ToolArgs args, CancellationToken ct)
    {
        var description = args.Get("description");
        if (string.IsNullOrWhiteSpace(description))
            return new { error = "description parameter is required." };

        // Search knowledge base for relevant topics
        var knowledgeResults = _configKnowledge != null
            ? await _configKnowledge.SearchAsync(description, ct)
            : null;

        // Get current health status
        HealthReport? healthReport = null;
        if (_configHealthService != null)
        {
            var context = await BuildHealthContextAsync(ct);
            healthReport = await _configHealthService.EvaluateAsync(context, false, ct);
        }

        // Build response lists (avoid ?? on incompatible anonymous types)
        var topics = new List<object>();
        if (knowledgeResults != null)
        {
            foreach (var r in knowledgeResults.Results)
            {
                topics.Add(new { r.TopicId, r.Title, r.Summary, r.RelevanceScore });
            }
        }

        var issues = new List<object>();
        if (healthReport != null)
        {
            foreach (var i in healthReport.Issues.Where(i => i.Level == "Critical" || i.Level == "Warning"))
            {
                issues.Add(new { i.RuleId, i.Title, i.Recommendation });
            }
        }

        return new
        {
            userRequest = description,
            relevantTopics = topics,
            currentHealthScore = healthReport?.Score,
            currentIssues = issues,
            message = "Based on your description, here are relevant configuration topics and any current issues to address."
        };
    }

    // ───────────── get_config_templates ─────────────

    private async Task<object> ExecuteGetConfigTemplates(ToolArgs args)
    {
        if (_configTemplateService == null)
            return new { error = "Template service is not available." };

        var category = args.Get("category");
        var templates = await _configTemplateService.GetAllAsync();

        var filtered = string.IsNullOrEmpty(category)
            ? templates
            : templates.Where(t => t.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

        return new
        {
            count = filtered.Count,
            templates = filtered.Select(t => new
            {
                t.Id,
                t.Name,
                t.Description,
                t.Category,
                t.Difficulty,
                t.Features,
                variableCount = t.Variables.Count,
                t.Steps
            })
        };
    }

    // ───────────── apply_config_template ─────────────

    private async Task<object> ExecuteApplyConfigTemplateAsync(ToolArgs args, CancellationToken ct)
    {
        if (_configTemplateService == null)
            return new { error = "Template service is not available." };

        var templateId = args.Get("template_id");
        if (string.IsNullOrWhiteSpace(templateId))
            return new { error = "template_id parameter is required." };

        // Parse variables from args
        var variables = new Dictionary<string, string>();
        var variablesArg = args.GetStringMap("variables");
        if (variablesArg != null)
        {
            foreach (var kvp in variablesArg)
                variables[kvp.Key] = kvp.Value;
        }

        var result = await _configTemplateService.ApplyAsync(templateId, variables, ct);

        return new
        {
            result.Success,
            result.Message,
            result.ImportedRoutes,
            result.ImportedClusters
        };
    }

    // ───────────── Health Context Builder ─────────────

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

            // Check transform types
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

            // Check if retry is configured via metadata
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

            // Check circuit breaker in cluster metadata
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

        // Also check policy service for circuit breakers
        try
        {
            var clusterPolicies = await _policyService.GetAllClusterPoliciesAsync();
            foreach (var policy in clusterPolicies)
            {
                if (policy.CircuitBreaker?.Enabled == true)
                {
                    foreach (var clusterId in policy.AppliedClusters)
                    {
                        context.ClustersWithCircuitBreaker.Add(clusterId);
                    }
                }
            }

            var routePolicies = await _policyService.GetAllRoutePoliciesAsync();
            foreach (var policy in routePolicies)
            {
                if (policy.Retry?.Enabled == true)
                {
                    foreach (var routeId in policy.AppliedRoutes)
                    {
                        context.RoutesWithRetry.Add(routeId);
                    }
                }
            }

            context.CircuitBreakerPolicyCount = clusterPolicies.Count;
            context.RetryPolicyCount = routePolicies.Count(p => p.Retry?.Enabled == true);
        }
        catch
        {
            // Policy service may not be available, continue without it
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
            catch
            {
                // WAF settings may not be available
            }
        }

        // Get snapshot count
        try
        {
            var history = await _configPersistence.GetHistoryAsync();
            context.SnapshotCount = history?.Count ?? 0;
        }
        catch
        {
            // Snapshot service may not be available
        }

        return context;
    }
}
