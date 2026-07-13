using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Services;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Services;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Tools;

/// <summary>
/// Executes gateway tool calls requested by the AI model.
/// Dispatches each tool to the appropriate service method and returns structured results.
/// </summary>
public class GatewayToolExecutor
{
    private readonly IDynamicYarpConfigService _dynamicConfig;
    private readonly IDashboardRouteQueryService _routeQuery;
    private readonly IDashboardClusterQueryService _clusterQuery;
    private readonly IDashboardLogQueryService _logQuery;
    private readonly IProxyLogRepository _logRepo;
    private readonly ICircuitStateStore _circuitStore;
    private readonly IGatewayPluginManager _pluginManager;
    private readonly IWafSettingsPersistenceService? _wafPersistence;
    private readonly RateLimitOptions _rateLimitOptions;
    private readonly RetryOptions _retryOptions;
    private readonly IRateLimiterStore _rateLimiterStore;
    private readonly ConfigChangeAuditLog _auditLog;
    private readonly IDashboardInfoQueryService _infoService;
    private readonly WafEventStore _wafEventStore;
    private readonly IConfigPersistenceService _configPersistence;
    private readonly IConfigSnapshotScheduler _snapshotScheduler;
    private readonly INotificationRepository _notificationRepo;
    private readonly LogSettingsService _logSettingsService;
    private readonly DeploymentRestartState _restartState;
    private readonly IGatewayPolicyService _policyService;
    private readonly ILogger<GatewayToolExecutor> _logger;

    public GatewayToolExecutor(
        IDynamicYarpConfigService dynamicConfig,
        IDashboardRouteQueryService routeQuery,
        IDashboardClusterQueryService clusterQuery,
        IDashboardLogQueryService logQuery,
        IProxyLogRepository logRepo,
        ICircuitStateStore circuitStore,
        IGatewayPluginManager pluginManager,
        IOptions<RateLimitOptions> rateLimitOptions,
        IOptions<RetryOptions> retryOptions,
        IRateLimiterStore rateLimiterStore,
        ConfigChangeAuditLog auditLog,
        IDashboardInfoQueryService infoService,
        WafEventStore wafEventStore,
        IConfigPersistenceService configPersistence,
        IConfigSnapshotScheduler snapshotScheduler,
        INotificationRepository notificationRepo,
        LogSettingsService logSettingsService,
        DeploymentRestartState restartState,
        IGatewayPolicyService policyService,
        ILogger<GatewayToolExecutor> logger,
        IWafSettingsPersistenceService? wafPersistence = null)
    {
        _dynamicConfig = dynamicConfig;
        _routeQuery = routeQuery;
        _clusterQuery = clusterQuery;
        _logQuery = logQuery;
        _logRepo = logRepo;
        _circuitStore = circuitStore;
        _pluginManager = pluginManager;
        _rateLimitOptions = rateLimitOptions.Value;
        _retryOptions = retryOptions.Value;
        _rateLimiterStore = rateLimiterStore;
        _auditLog = auditLog;
        _infoService = infoService;
        _wafEventStore = wafEventStore;
        _configPersistence = configPersistence;
        _snapshotScheduler = snapshotScheduler;
        _notificationRepo = notificationRepo;
        _logSettingsService = logSettingsService;
        _restartState = restartState;
        _policyService = policyService;
        _wafPersistence = wafPersistence;
        _logger = logger;
    }

    /// <summary>
    /// Execute a tool call and return structured result.
    /// </summary>
    public async Task<AIToolResult> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var args = string.IsNullOrEmpty(argumentsJson)
                ? JsonDocument.Parse("{}").RootElement
                : JsonDocument.Parse(argumentsJson).RootElement;

            object data = toolName switch
            {
                // Read tools
                "get_routes" => ExecuteGetRoutes(),
                "get_clusters" => ExecuteGetClusters(),
                "get_circuit_status" => ExecuteGetCircuitStatus(),
                "get_proxy_logs" => ExecuteGetProxyLogs(args),
                "get_health_summary" => ExecuteGetHealthSummary(),
                "get_plugins" => ExecuteGetPlugins(),
                "get_waf_settings" => ExecuteGetWafSettings(),
                "search_logs" => await ExecuteSearchLogsAsync(args, ct),
                "get_traffic_stats" => await ExecuteGetTrafficStatsAsync(args, ct),
                "get_rate_limit_status" => ExecuteGetRateLimitStatus(),
                "get_retry_config" => ExecuteGetRetryConfig(),
                "get_audit_log" => ExecuteGetAuditLog(args),
                "get_gateway_info" => ExecuteGetGatewayInfo(),
                "get_deployment_info" => ExecuteGetDeploymentInfo(),
                "get_alert_summary" => await ExecuteGetAlertSummaryAsync(ct),
                "get_security_events" => ExecuteGetSecurityEvents(args),
                "get_health_check_config" => ExecuteGetHealthCheckConfig(),
                "get_top_issues" => await ExecuteGetTopIssuesAsync(args, ct),
                "export_config" => await ExecuteExportConfigAsync(),
                "get_config_history" => await ExecuteGetConfigHistoryAsync(),
                "get_notification_summary" => await ExecuteGetNotificationSummaryAsync(ct),
                "get_policies" => await ExecuteGetPoliciesAsync(),

                // Write tools
                "create_route" => await ExecuteCreateRouteAsync(args, ct),
                "delete_route" => await ExecuteDeleteRouteAsync(args, ct),
                "create_cluster" => await ExecuteCreateClusterAsync(args, ct),
                "update_cluster" => await ExecuteUpdateClusterAsync(args, ct),
                "delete_cluster" => await ExecuteDeleteClusterAsync(args, ct),
                "create_circuit_breaker" => await ExecuteCreateCircuitBreakerAsync(args),
                "reset_circuit_breaker" => ExecuteResetCircuitBreaker(args),
                "toggle_plugin" => ExecuteTogglePlugin(args),
                "update_waf_settings" => ExecuteUpdateWafSettings(args),
                "rename_route" => await ExecuteRenameRouteAsync(args),
                "rename_cluster" => await ExecuteRenameClusterAsync(args),
                "clear_logs" => ExecuteClearLogs(),
                "create_config_snapshot" => await ExecuteCreateConfigSnapshotAsync(args),
                "rollback_config" => await ExecuteRollbackConfigAsync(args),
                "create_cluster_policy" => await ExecuteCreateClusterPolicyAsync(args),
                "apply_cluster_policy" => await ExecuteApplyClusterPolicyAsync(args),
                "create_route_policy" => await ExecuteCreateRoutePolicyAsync(args),
                "apply_route_policy" => await ExecuteApplyRoutePolicyAsync(args),
                "delete_policy" => await ExecuteDeletePolicyAsync(args),

                _ => throw new InvalidOperationException($"Unknown tool: {toolName}")
            };

            return new AIToolResult
            {
                ToolName = toolName,
                Success = true,
                Data = data
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool execution failed: {ToolName}", toolName);
            return new AIToolResult
            {
                ToolName = toolName,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    // ===================== READ TOOLS =====================

    private object ExecuteGetRoutes()
    {
        var routes = _routeQuery.GetRoutes();
        return new
        {
            total = routes.Count,
            routes = routes.Select(r => new
            {
                route_id = r.RouteId,
                cluster_id = r.ClusterId,
                path = r.Match?.Path ?? "*",
                methods = r.Match?.Methods,
                source = r.Source,
                order = r.Order
            })
        };
    }

    private object ExecuteGetClusters()
    {
        var clusters = _clusterQuery.GetClusters();
        return new
        {
            total = clusters.Count,
            clusters = clusters.Select(c => new
            {
                cluster_id = c.ClusterId,
                load_balancing = c.LoadBalancingPolicy,
                destinations = c.Destinations?.Select(d => new
                {
                    name = d.Name,
                    address = d.Address,
                    health = d.Health
                })
            })
        };
    }

    private object ExecuteGetCircuitStatus()
    {
        var allStates = _circuitStore.GetAll();
        var openCircuits = allStates.Count(s => s.Value.Status == CircuitStatus.Open);
        var halfOpen = allStates.Count(s => s.Value.Status == CircuitStatus.HalfOpen);
        var closed = allStates.Count(s => s.Value.Status == CircuitStatus.Closed);

        return new
        {
            total = allStates.Count,
            open = openCircuits,
            half_open = halfOpen,
            closed,
            circuits = allStates.Select(s => new
            {
                key = s.Key,
                cluster = s.Value.ClusterKeySnapshot,
                status = s.Value.Status.ToString(),
                consecutive_failures = s.Value.ConsecutiveFailures,
                failure_threshold = s.Value.FailureThreshold
            })
        };
    }

    private object ExecuteGetProxyLogs(JsonElement args)
    {
        var count = args.TryGetProperty("count", out var c) ? c.GetInt32() : 50;
        count = Math.Clamp(count, 1, 200);

        var snapshot = _logQuery.GetLogs(count);
        var entries = snapshot.Entries;

        var statusDist = entries
            .GroupBy(e => e.StatusCode / 100)
            .OrderBy(g => g.Key)
            .Select(g => new { group = g.Key + "xx", count = g.Count() })
            .ToList();

        return new
        {
            total = entries.Count,
            status_distribution = statusDist,
            entries = entries.Select(e => new
            {
                route_id = e.RouteId,
                method = e.Method,
                path = e.UpstreamPath,
                status_code = e.StatusCode,
                elapsed_ms = e.ElapsedMs,
                timestamp = e.Timestamp.ToString("O")
            })
        };
    }

    private object ExecuteGetHealthSummary()
    {
        var clusters = _clusterQuery.GetClusters();
        var totalDest = 0;
        var healthy = 0;
        var unhealthy = 0;
        var unknown = 0;

        foreach (var cluster in clusters)
        {
            if (cluster.Destinations == null) continue;
            foreach (var dest in cluster.Destinations)
            {
                totalDest++;
                var health = dest.Health;
                if (string.IsNullOrEmpty(health)) unknown++;
                else if (health.Equals("Healthy", StringComparison.OrdinalIgnoreCase)) healthy++;
                else unhealthy++;
            }
        }

        return new
        {
            total_clusters = clusters.Count,
            total_destinations = totalDest,
            healthy,
            unhealthy,
            unknown,
            clusters = clusters.Select(c => new
            {
                cluster_id = c.ClusterId,
                destinations = c.Destinations?.Select(d => new
                {
                    name = d.Name,
                    address = d.Address,
                    health = d.Health ?? "Unknown"
                })
            })
        };
    }

    private object ExecuteGetPlugins()
    {
        var plugins = _pluginManager.GetAllPlugins();
        return new
        {
            total = plugins.Count,
            plugins = plugins.Select(p => new
            {
                plugin_id = p.PluginId,
                display_name = p.DisplayName,
                version = p.Version,
                enabled = _pluginManager.IsPluginEnabled(p.PluginId)
            })
        };
    }

    private object ExecuteGetWafSettings()
    {
        var data = _wafPersistence?.Load();
        if (data == null)
            return new { message = "WAF settings not available" };

        return new
        {
            enabled = data.Enabled,
            enable_ip_check = data.EnableIpCheck,
            ip_whitelist = data.IpWhitelist,
            ip_blacklist = data.IpBlacklist,
            enable_request_size_validation = data.EnableRequestSizeValidation,
            max_request_body_size = data.MaxRequestBodySize,
            enable_sql_injection = data.EnableSqlInjectionDetection,
            enable_xss = data.EnableXssDetection,
            enable_path_traversal = data.EnablePathTraversalDetection
        };
    }

    // ===================== NEW READ TOOLS =====================

    private async Task<object> ExecuteSearchLogsAsync(JsonElement args, CancellationToken ct)
    {
        var count = args.TryGetProperty("count", out var c) ? Math.Clamp(c.GetInt32(), 1, 200) : 50;
        var minutes = args.TryGetProperty("time_range_minutes", out var tr) ? tr.GetInt32() : 60;
        var routeId = args.TryGetProperty("route_id", out var ri) && ri.ValueKind == JsonValueKind.String ? ri.GetString() : null;
        var statusMin = args.TryGetProperty("status_code_min", out var smin) ? smin.GetInt32() : (int?)null;
        var statusMax = args.TryGetProperty("status_code_max", out var smax) ? smax.GetInt32() : (int?)null;
        var keyword = args.TryGetProperty("keyword", out var kw) && kw.ValueKind == JsonValueKind.String ? kw.GetString() : null;

        var startTime = DateTime.UtcNow.AddMinutes(-minutes);
        var (items, totalCount) = await _logRepo.SearchAsync(
            page: 1, pageSize: count,
            routeId: routeId, statusCodeMin: statusMin, statusCodeMax: statusMax,
            startTime: startTime, keyword: keyword, ct: ct);

        return new
        {
            total = totalCount,
            returned = items.Count,
            time_range_minutes = minutes,
            entries = items.Select(e => new
            {
                route_id = e.RouteId,
                method = e.Method,
                path = e.UpstreamPath,
                status_code = e.StatusCode,
                latency_ms = e.ElapsedMs,
                timestamp = e.Timestamp
            })
        };
    }

    private async Task<object> ExecuteGetTrafficStatsAsync(JsonElement args, CancellationToken ct)
    {
        var minutes = args.TryGetProperty("time_range_minutes", out var tr) ? tr.GetInt32() : 60;
        var stats = await _logRepo.GetStatsAsync(minutes, ct);
        var startTime = DateTime.UtcNow.AddMinutes(-minutes);
        var topIssues = await _logRepo.GetTopIssuesAsync(startTime, 5, ct);
        var recent5xx = await _logRepo.GetRecent5xxCountAsync(minutes, ct);

        return new
        {
            time_range_minutes = minutes,
            total_requests = stats.TotalRequests,
            success_count = stats.SuccessCount,
            error_count = stats.ErrorCount,
            error_rate = stats.TotalRequests > 0
                ? Math.Round((double)stats.ErrorCount / stats.TotalRequests * 100, 2)
                : 0,
            avg_latency_ms = Math.Round(stats.AvgLatencyMs, 2),
            p50_latency_ms = Math.Round(stats.P50LatencyMs, 2),
            p90_latency_ms = Math.Round(stats.P90LatencyMs, 2),
            p99_latency_ms = Math.Round(stats.P99LatencyMs, 2),
            requests_per_minute = stats.RequestsPerMinute,
            recent_5xx_count = recent5xx,
            top_error_routes = topIssues.Select(i => new
            {
                route_id = i.RouteId,
                total = i.TotalCount,
                errors = i.ErrorCount
            })
        };
    }

    private object ExecuteGetRateLimitStatus()
    {
        return new
        {
            enabled = _rateLimitOptions.Enabled,
            algorithm = _rateLimitOptions.Algorithm.ToString(),
            permit_limit = _rateLimitOptions.PermitLimit,
            window = _rateLimitOptions.Window,
            queue_limit = _rateLimitOptions.QueueLimit,
            active_limiters = _rateLimiterStore.Count
        };
    }

    private object ExecuteGetRetryConfig()
    {
        return new
        {
            enabled = _retryOptions.Enabled,
            max_retries = _retryOptions.DefaultMaxRetries,
            backoff_base_ms = _retryOptions.BackoffBaseMs
        };
    }

    private object ExecuteGetAuditLog(JsonElement args)
    {
        var count = args.TryGetProperty("count", out var c) ? Math.Clamp(c.GetInt32(), 1, 100) : 20;
        var actionFilter = args.TryGetProperty("action_filter", out var af) && af.ValueKind == JsonValueKind.String
            ? af.GetString() : null;

        var (entries, total) = _auditLog.GetPage(page: 1, pageSize: count, action: actionFilter);

        return new
        {
            total,
            returned = entries.Count,
            entries = entries.Select(e => new
            {
                action = e.Action,
                target = e.Target,
                @operator = e.Operator,
                client_ip = e.ClientIp,
                status = e.Success ? "success" : "failed",
                error = e.ErrorMessage,
                timestamp = e.Timestamp.ToString("O")
            })
        };
    }

    // ===================== WRITE TOOLS =====================

    private async Task<object> ExecuteCreateRouteAsync(JsonElement args, CancellationToken ct)
    {
        var routeName = args.GetProperty("route_name").GetString()!;
        var path = args.GetProperty("path").GetString()!;
        var clusterId = args.GetProperty("cluster_id").GetString()!;
        var destAddress = args.GetProperty("destination_address").GetString()!;

        var request = new RegisterRouteRequest
        {
            RouteName = routeName,
            MatchPath = path,
            ClusterName = clusterId,
            DestinationAddress = destAddress
        };

        var result = await _dynamicConfig.TryAddRoute(request, "ai-assistant", "ai");
        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Route '{routeName}' created/updated successfully."
                : $"Failed to create route: {result.Message}",
            route_id = routeName
        };
    }

    private async Task<object> ExecuteDeleteRouteAsync(JsonElement args, CancellationToken ct)
    {
        var routeId = args.GetProperty("route_id").GetString()!;
        var result = await _dynamicConfig.TryRemoveRoute(routeId, "ai-assistant");
        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Route '{routeId}' deleted successfully."
                : $"Failed to delete route: {result.Message}"
        };
    }

    private async Task<object> ExecuteCreateClusterAsync(JsonElement args, CancellationToken ct)
    {
        var clusterId = args.GetProperty("cluster_id").GetString()!;
        var destElement = args.GetProperty("destinations");
        var destinations = new Dictionary<string, string>();
        foreach (var prop in destElement.EnumerateObject())
        {
            destinations[prop.Name] = prop.Value.GetString()!;
        }

        var lb = args.TryGetProperty("load_balancing", out var lbProp)
            ? lbProp.GetString()
            : "RoundRobin";

        var request = new CreateClusterRequest
        {
            ClusterId = clusterId,
            Destinations = destinations,
            LoadBalancingPolicy = lb
        };

        var result = await _dynamicConfig.TryAddCluster(request, "ai-assistant", "ai");
        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Cluster '{clusterId}' created with {destinations.Count} destination(s)."
                : $"Failed to create cluster: {result.Message}",
            cluster_id = clusterId
        };
    }

    private async Task<object> ExecuteUpdateClusterAsync(JsonElement args, CancellationToken ct)
    {
        var clusterId = args.GetProperty("cluster_id").GetString()!;
        var updateReq = new UpdateClusterRequest();

        if (args.TryGetProperty("destinations", out var destElement) && destElement.ValueKind == JsonValueKind.Object)
        {
            var destinations = new Dictionary<string, string>();
            foreach (var prop in destElement.EnumerateObject())
            {
                destinations[prop.Name] = prop.Value.GetString()!;
            }
            updateReq.Destinations = destinations;
        }

        if (args.TryGetProperty("load_balancing", out var lbProp))
        {
            updateReq.LoadBalancingPolicy = lbProp.GetString();
        }

        var result = await _dynamicConfig.TryUpdateCluster(clusterId, updateReq);
        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Cluster '{clusterId}' updated."
                : $"Failed to update cluster: {result.Message}"
        };
    }

    private async Task<object> ExecuteDeleteClusterAsync(JsonElement args, CancellationToken ct)
    {
        var clusterId = args.GetProperty("cluster_id").GetString()!;
        var result = await _dynamicConfig.TryRemoveCluster(clusterId);
        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Cluster '{clusterId}' deleted."
                : $"Failed to delete cluster: {result.Message}"
        };
    }

    private async Task<object> ExecuteCreateCircuitBreakerAsync(JsonElement args)
    {
        var clusterId = args.GetProperty("cluster_id").GetString()!;

        // Check if cluster exists
        var clusters = _clusterQuery.GetClusters();
        if (clusters.All(c => !string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase)))
            return new { success = false, message = $"Cluster '{clusterId}' not found. Create the cluster first." };

        var enabled = args.TryGetProperty("enabled", out var en) && en.ValueKind != JsonValueKind.Null
            ? en.GetBoolean() : true;

        if (!enabled)
        {
            // Remove / disable circuit breaker for this cluster
            var removed = await _dynamicConfig.UpdateClusterCircuitBreakerAsync(clusterId, null);
            return new
            {
                success = removed,
                cluster_id = clusterId,
                message = removed
                    ? $"Circuit breaker disabled for cluster '{clusterId}'."
                    : $"Failed to disable circuit breaker for cluster '{clusterId}'."
            };
        }

        var config = new CircuitBreakerConfig
        {
            Enabled = true,
            FailureThreshold = args.TryGetProperty("failure_threshold", out var ft) && ft.ValueKind != JsonValueKind.Null
                ? ft.GetInt32() : 5,
            RecoveryTimeoutSeconds = args.TryGetProperty("recovery_timeout_seconds", out var rt) && rt.ValueKind != JsonValueKind.Null
                ? rt.GetInt32() : 30,
            HalfOpenMaxAttempts = args.TryGetProperty("half_open_max_attempts", out var ho) && ho.ValueKind != JsonValueKind.Null
                ? ho.GetInt32() : 1,
            FailureStatusCodes = args.TryGetProperty("failure_status_codes", out var fsc) && fsc.ValueKind == JsonValueKind.Array
                ? fsc.EnumerateArray().Select(e => e.GetInt32()).ToList()
                : new List<int> { 500, 502, 503, 504 }
        };

        var success = await _dynamicConfig.UpdateClusterCircuitBreakerAsync(clusterId, config);
        return new
        {
            success,
            cluster_id = clusterId,
            config = new
            {
                enabled = true,
                failure_threshold = config.FailureThreshold,
                recovery_timeout_seconds = config.RecoveryTimeoutSeconds,
                half_open_max_attempts = config.HalfOpenMaxAttempts,
                failure_status_codes = config.FailureStatusCodes
            },
            message = success
                ? $"Circuit breaker created for cluster '{clusterId}': threshold={config.FailureThreshold}, recovery={config.RecoveryTimeoutSeconds}s, half-open max={config.HalfOpenMaxAttempts}."
                : $"Failed to create circuit breaker for cluster '{clusterId}'."
        };
    }

    private object ExecuteResetCircuitBreaker(JsonElement args)
    {
        var clusterId = args.TryGetProperty("cluster_id", out var cid) ? cid.GetString() : null;

        if (!string.IsNullOrEmpty(clusterId))
        {
            // Reset specific cluster
            if (_circuitStore.TryGet(clusterId, out var state) && state != null)
            {
                lock (state.SyncRoot)
                {
                    state.Status = CircuitStatus.Closed;
                    state.ConsecutiveFailures = 0;
                    state.HalfOpenRequests = 0;
                }
                return new
                {
                    cluster_id = clusterId,
                    status = "Closed",
                    message = $"Circuit for '{clusterId}' reset to Closed."
                };
            }
            return new { cluster_id = clusterId, message = $"No circuit found for '{clusterId}'." };
        }

        // Reset all
        _circuitStore.ResetAll();
        var total = _circuitStore.Count;
        return new
        {
            cluster_id = "all",
            total,
            message = $"All {total} circuit(s) reset to Closed."
        };
    }

    private object ExecuteTogglePlugin(JsonElement args)
    {
        var pluginId = args.GetProperty("plugin_id").GetString()!;
        var enabled = args.GetProperty("enabled").GetBoolean();

        var plugins = _pluginManager.GetAllPlugins();
        if (plugins.All(p => !string.Equals(p.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Plugin '{pluginId}' not found.");

        _pluginManager.SetPluginEnabled(pluginId, enabled);
        return new
        {
            plugin_id = pluginId,
            enabled,
            message = $"Plugin '{pluginId}' {(enabled ? "enabled" : "disabled")}."
        };
    }

    private object ExecuteUpdateWafSettings(JsonElement args)
    {
        if (_wafPersistence == null)
            return new { success = false, message = "WAF persistence service not available." };

        var data = _wafPersistence.Load() ?? new WafSettingsData();

        if (args.TryGetProperty("enabled", out var en) && en.ValueKind != JsonValueKind.Null)
            data.Enabled = en.GetBoolean();
        if (args.TryGetProperty("enable_sql_injection", out var sqli) && sqli.ValueKind != JsonValueKind.Null)
            data.EnableSqlInjectionDetection = sqli.GetBoolean();
        if (args.TryGetProperty("enable_xss", out var xss) && xss.ValueKind != JsonValueKind.Null)
            data.EnableXssDetection = xss.GetBoolean();
        if (args.TryGetProperty("enable_path_traversal", out var pt) && pt.ValueKind != JsonValueKind.Null)
            data.EnablePathTraversalDetection = pt.GetBoolean();
        if (args.TryGetProperty("ip_blacklist", out var bl) && bl.ValueKind == JsonValueKind.Array)
        {
            data.IpBlacklist.Clear();
            foreach (var ip in bl.EnumerateArray())
            {
                var ipStr = ip.GetString();
                if (!string.IsNullOrEmpty(ipStr))
                    data.IpBlacklist.Add(ipStr);
            }
        }

        var saved = _wafPersistence.Save(data);
        return new
        {
            success = saved,
            message = saved ? "WAF settings updated." : "Failed to save WAF settings."
        };
    }

    // ===================== NEW READ TOOLS =====================

    private object ExecuteGetGatewayInfo()
    {
        var info = _infoService.GetInfo();
        return new
        {
            version = info.Version,
            environment = info.Environment,
            start_time = info.StartTime,
            uptime = info.Uptime,
            memory_mb = Math.Round(info.MemoryMb, 2),
            cpu_usage = Math.Round(info.CpuUsage, 1),
            machine_name = info.MachineName,
            process_id = info.ProcessId,
            thread_count = info.ThreadCount,
            gc_count = info.GcCount
        };
    }

    private object ExecuteGetDeploymentInfo()
    {
        var restartReasons = _restartState.GetReasons();
        return new
        {
            restart_required = _restartState.IsRestartRequired,
            restart_reasons = restartReasons.Select(r => new
            {
                title = r.Title,
                message = r.Message,
                config_path = r.ConfigPath
            }),
            circuit_breaker_states = _circuitStore.GetAll().Count
        };
    }

    private async Task<object> ExecuteGetAlertSummaryAsync(CancellationToken ct)
    {
        var clusters = _clusterQuery.GetClusters();
        var totalDest = 0;
        var unhealthyDest = 0;
        foreach (var c in clusters)
        {
            totalDest += c.TotalCount;
            unhealthyDest += c.UnhealthyCount;
        }

        var openCircuits = _circuitStore.GetAll().Count(s => s.Value.Status == CircuitStatus.Open);
        var recent5xx = await _logRepo.GetRecent5xxCountAsync(60, ct);
        var snapshot = _logQuery.GetLogs(100);
        var errorCount = snapshot.Entries.Count(e => e.StatusCode >= 500);

        return new
        {
            unhealthy_destinations = unhealthyDest,
            total_destinations = totalDest,
            open_circuits = openCircuits,
            recent_5xx_last_60min = recent5xx,
            error_count_last_100_requests = errorCount,
            has_alerts = unhealthyDest > 0 || openCircuits > 0 || recent5xx > 5
        };
    }

    private object ExecuteGetSecurityEvents(JsonElement args)
    {
        var count = args.TryGetProperty("count", out var c) ? Math.Clamp(c.GetInt32(), 1, 200) : 50;
        var events = _wafEventStore.GetRecent(count);
        return new
        {
            total = _wafEventStore.Count,
            returned = events.Count,
            dropped_count = _wafEventStore.DroppedCount,
            events = events.Select(e => new
            {
                id = e.Id,
                timestamp = e.Timestamp.ToString("O"),
                event_type = e.EventType,
                rule_name = e.RuleName,
                client_ip = e.ClientIp,
                request_uri = e.RequestUri,
                request_method = e.RequestMethod,
                blocked = e.Blocked
            })
        };
    }

    private object ExecuteGetHealthCheckConfig()
    {
        var dynConfig = _dynamicConfig.GetDynamicConfig();
        if (dynConfig?.Clusters == null)
            return new { total = 0, clusters = Array.Empty<object>() };

        var result = dynConfig.Clusters
            .Where(c => c.HealthCheck != null)
            .Select(c => new
            {
                cluster_id = c.Config?.ClusterId ?? c.ClusterUid,
                active_enabled = c.HealthCheck!.Active,
                active_endpoint = c.HealthCheck.Endpoint,
                passive_enabled = c.HealthCheck.Passive,
                passive_policy = c.HealthCheck.PassivePolicy,
                available_destinations_policy = c.HealthCheck.AvailableDestinationsPolicy
            })
            .ToList();

        return new
        {
            total = result.Count,
            clusters = result
        };
    }

    private async Task<object> ExecuteGetTopIssuesAsync(JsonElement args, CancellationToken ct)
    {
        var minutes = args.TryGetProperty("time_range_minutes", out var tr) ? tr.GetInt32() : 60;
        var count = args.TryGetProperty("count", out var c) ? Math.Clamp(c.GetInt32(), 1, 20) : 5;
        var startTime = DateTime.UtcNow.AddMinutes(-minutes);
        var issues = await _logRepo.GetTopIssuesAsync(startTime, count, ct);

        return new
        {
            time_range_minutes = minutes,
            top_issues = issues.Select(i => new
            {
                route_id = i.RouteId,
                total_count = i.TotalCount,
                error_count = i.ErrorCount
            })
        };
    }

    private async Task<object> ExecuteExportConfigAsync()
    {
        var config = await _configPersistence.ExportFullConfigAsync();
        return new
        {
            success = true,
            config = config,
            message = "Full YARP configuration exported."
        };
    }

    private async Task<object> ExecuteGetConfigHistoryAsync()
    {
        var history = await _configPersistence.GetHistoryAsync();
        return new
        {
            total = history.Count,
            snapshots = history.Select(h => new
            {
                version_id = h.VersionId,
                timestamp = h.Timestamp.ToString("O"),
                description = h.Description,
                client_ip = h.ClientIp
            })
        };
    }

    private async Task<object> ExecuteGetNotificationSummaryAsync(CancellationToken ct)
    {
        var channels = await _notificationRepo.GetChannelsAsync(ct);
        var rules = await _notificationRepo.GetRulesAsync(ct);
        var globalSettings = await _notificationRepo.GetGlobalSettingsAsync(ct);
        var (records, total) = await _notificationRepo.GetHistoryAsync(page: 1, pageSize: 10, ct: ct);

        return new
        {
            channels_count = channels.Count,
            rules_count = rules.Count,
            enabled = globalSettings.Enabled,
            recent_history_count = total,
            channels = channels.Select(ch => new
            {
                id = ch.Id,
                name = ch.Name,
                type = ch.Type.ToString(),
                enabled = ch.Enabled
            }),
            rules = rules.Select(r => new
            {
                id = r.Id,
                name = r.Name,
                event_types = r.EventTypes,
                enabled = r.Enabled
            })
        };
    }

    // ===================== NEW WRITE TOOLS =====================

    private async Task<object> ExecuteRenameRouteAsync(JsonElement args)
    {
        var oldRouteId = args.GetProperty("old_route_id").GetString()!;
        var newRouteId = args.GetProperty("new_route_id").GetString()!;

        // Build a RegisterRouteRequest from existing route
        var routes = _routeQuery.GetRoutes();
        var existing = routes.FirstOrDefault(r =>
            string.Equals(r.RouteId, oldRouteId, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
            return new { success = false, message = $"Route '{oldRouteId}' not found." };

        var request = new RegisterRouteRequest
        {
            RouteName = newRouteId,
            MatchPath = existing.Match?.Path ?? "/",
            ClusterName = existing.ClusterId,
            DestinationAddress = existing.Destinations?.FirstOrDefault().Address ?? ""
        };

        var result = await _dynamicConfig.TryRenameRoute(oldRouteId, newRouteId, request, "ai-assistant", "ai");
        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Route renamed: '{oldRouteId}' → '{newRouteId}'."
                : $"Failed to rename route: {result.Message}"
        };
    }

    private async Task<object> ExecuteRenameClusterAsync(JsonElement args)
    {
        var oldClusterId = args.GetProperty("old_cluster_id").GetString()!;
        var newClusterId = args.GetProperty("new_cluster_id").GetString()!;

        var clusters = _clusterQuery.GetClusters();
        var existing = clusters.FirstOrDefault(c =>
            string.Equals(c.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
            return new { success = false, message = $"Cluster '{oldClusterId}' not found." };

        var destinations = existing.Destinations?
            .ToDictionary(d => d.Name, d => d.Address) ?? new Dictionary<string, string>();

        var result = await _dynamicConfig.TryRenameCluster(
            oldClusterId, newClusterId, destinations,
            existing.LoadBalancingPolicy, null, "ai-assistant", "ai");

        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Cluster renamed: '{oldClusterId}' → '{newClusterId}'."
                : $"Failed to rename cluster: {result.Message}"
        };
    }

    private object ExecuteClearLogs()
    {
        _logQuery.ClearLogs();
        return new
        {
            success = true,
            message = "All in-memory proxy logs cleared."
        };
    }

    private async Task<object> ExecuteCreateConfigSnapshotAsync(JsonElement args)
    {
        var description = args.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String
            ? desc.GetString() : "AI-assistant snapshot";

        var snapshot = await _configPersistence.SaveSnapshotAsync(description, "ai-assistant");
        return new
        {
            success = true,
            version_id = snapshot.VersionId,
            timestamp = snapshot.Timestamp.ToString("O"),
            message = $"Config snapshot created: {snapshot.VersionId}."
        };
    }

    private async Task<object> ExecuteRollbackConfigAsync(JsonElement args)
    {
        var versionId = args.GetProperty("version_id").GetString()!;
        var success = await _configPersistence.RollbackAsync(versionId, "ai-assistant");
        return new
        {
            success,
            version_id = versionId,
            message = success
                ? $"Config rolled back to version '{versionId}'."
                : $"Failed to rollback to version '{versionId}'."
        };
    }

    // ===================== POLICY TOOLS =====================

    private async Task<object> ExecuteGetPoliciesAsync()
    {
        var routePolicies = await _policyService.GetAllRoutePoliciesAsync();
        var clusterPolicies = await _policyService.GetAllClusterPoliciesAsync();

        return new
        {
            route_policies_count = routePolicies.Count,
            cluster_policies_count = clusterPolicies.Count,
            route_policies = routePolicies.Select(p => new
            {
                policy_id = p.PolicyId,
                name = p.DisplayName,
                description = p.Description,
                enabled = p.Enabled,
                applied_routes = p.AppliedRoutes,
                has_retry = p.Retry != null,
                has_rate_limit = p.RateLimit != null,
                waf_enabled = p.WafEnabled
            }),
            cluster_policies = clusterPolicies.Select(p => new
            {
                policy_id = p.PolicyId,
                name = p.DisplayName,
                description = p.Description,
                enabled = p.Enabled,
                applied_clusters = p.AppliedClusters,
                circuit_breaker = p.CircuitBreaker != null ? new
                {
                    enabled = p.CircuitBreaker.Enabled,
                    failure_threshold = p.CircuitBreaker.FailureThreshold,
                    recovery_timeout_seconds = p.CircuitBreaker.RecoveryTimeoutSeconds,
                    half_open_max_attempts = p.CircuitBreaker.HalfOpenMaxAttempts
                } : null
            })
        };
    }

    private async Task<object> ExecuteCreateClusterPolicyAsync(JsonElement args)
    {
        var policyId = args.TryGetProperty("policy_id", out var pid) && pid.ValueKind == JsonValueKind.String
            ? pid.GetString() : null;
        var name = args.GetProperty("name").GetString()!;
        var description = args.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String
            ? desc.GetString() : null;

        var policy = new ClusterPolicy
        {
            PolicyId = policyId ?? Guid.NewGuid().ToString("N")[..12],
            DisplayName = name,
            Description = description,
            Enabled = true,
            CircuitBreaker = new PolicyCircuitBreaker
            {
                Enabled = true,
                FailureThreshold = args.TryGetProperty("failure_threshold", out var ft) ? ft.GetInt32() : 5,
                RecoveryTimeoutSeconds = args.TryGetProperty("recovery_timeout_seconds", out var rt) ? rt.GetInt32() : 30,
                HalfOpenMaxAttempts = args.TryGetProperty("half_open_max_attempts", out var ho) ? ho.GetInt32() : 1,
                FailureStatusCodes = args.TryGetProperty("failure_status_codes", out var fsc) && fsc.ValueKind == JsonValueKind.Array
                    ? fsc.EnumerateArray().Select(e => e.GetInt32()).ToList()
                    : new List<int> { 500, 502, 503, 504 }
            }
        };

        var created = await _policyService.CreateClusterPolicyAsync(policy);

        // Auto-apply to clusters if cluster_ids provided
        var appliedClusters = new List<string>();
        var failedClusters = new List<string>();
        if (args.TryGetProperty("cluster_ids", out var cids) && cids.ValueKind == JsonValueKind.Array)
        {
            foreach (var cidEl in cids.EnumerateArray())
            {
                var cid = cidEl.GetString();
                if (string.IsNullOrWhiteSpace(cid)) continue;
                var ok = await _policyService.ApplyClusterPolicyAsync(created.PolicyId, cid);
                if (ok) appliedClusters.Add(cid); else failedClusters.Add(cid);
            }
        }

        var appliedMsg = appliedClusters.Count > 0
            ? $" Applied to: {string.Join(", ", appliedClusters)}."
            : "";
        var failedMsg = failedClusters.Count > 0
            ? $" Failed on: {string.Join(", ", failedClusters)}."
            : "";

        return new
        {
            success = true,
            policy_id = created.PolicyId,
            name = created.DisplayName,
            applied_clusters = appliedClusters,
            failed_clusters = failedClusters,
            message = $"Cluster policy '{created.DisplayName}' created (ID: {created.PolicyId}).{appliedMsg}{failedMsg}"
        };
    }

    private async Task<object> ExecuteApplyClusterPolicyAsync(JsonElement args)
    {
        var policyId = args.GetProperty("policy_id").GetString()!;
        var clusterId = args.GetProperty("cluster_id").GetString()!;

        var success = await _policyService.ApplyClusterPolicyAsync(policyId, clusterId);
        return new
        {
            success,
            policy_id = policyId,
            cluster_id = clusterId,
            message = success
                ? $"Cluster policy '{policyId}' applied to cluster '{clusterId}'."
                : $"Failed to apply policy '{policyId}' to cluster '{clusterId}'."
        };
    }

    private async Task<object> ExecuteCreateRoutePolicyAsync(JsonElement args)
    {
        var policyId = args.TryGetProperty("policy_id", out var pid) && pid.ValueKind == JsonValueKind.String
            ? pid.GetString() : null;
        var name = args.GetProperty("name").GetString()!;
        var description = args.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String
            ? desc.GetString() : null;

        var policy = new RoutePolicy
        {
            PolicyId = policyId ?? Guid.NewGuid().ToString("N")[..12],
            DisplayName = name,
            Description = description,
            Enabled = true
        };

        // Optional retry config
        if (args.TryGetProperty("retry_enabled", out var re) && re.GetBoolean())
        {
            policy.Retry = new PolicyRetry
            {
                Enabled = true,
                MaxRetries = args.TryGetProperty("max_retries", out var mr) ? mr.GetInt32() : 3,
                BackoffBaseMs = args.TryGetProperty("backoff_base_ms", out var bb) ? bb.GetInt32() : 100,
                RetryStatusCodes = args.TryGetProperty("retry_status_codes", out var rsc) && rsc.ValueKind == JsonValueKind.Array
                    ? rsc.EnumerateArray().Select(e => e.GetInt32()).ToList()
                    : new List<int> { 502, 503, 504 }
            };
        }

        // Optional rate limit config
        if (args.TryGetProperty("rate_limit_enabled", out var rle) && rle.GetBoolean())
        {
            policy.RateLimit = new PolicyRateLimit
            {
                Enabled = true,
                PermitLimit = args.TryGetProperty("permit_limit", out var pl) ? pl.GetInt32() : 100,
                Window = args.TryGetProperty("window", out var w) && w.ValueKind == JsonValueKind.String
                    ? w.GetString()! : "1m",
                Algorithm = args.TryGetProperty("algorithm", out var alg) && alg.ValueKind == JsonValueKind.String
                    ? alg.GetString()! : "FixedWindow"
            };
        }

        var created = await _policyService.CreateRoutePolicyAsync(policy);

        // Auto-apply to routes if route_ids provided
        var appliedRoutes = new List<string>();
        var failedRoutes = new List<string>();
        if (args.TryGetProperty("route_ids", out var rids) && rids.ValueKind == JsonValueKind.Array)
        {
            foreach (var ridEl in rids.EnumerateArray())
            {
                var rid = ridEl.GetString();
                if (string.IsNullOrWhiteSpace(rid)) continue;
                var ok = await _policyService.ApplyRoutePolicyAsync(created.PolicyId, rid);
                if (ok) appliedRoutes.Add(rid); else failedRoutes.Add(rid);
            }
        }

        var appliedMsg = appliedRoutes.Count > 0
            ? $" Applied to: {string.Join(", ", appliedRoutes)}."
            : "";
        var failedMsg = failedRoutes.Count > 0
            ? $" Failed on: {string.Join(", ", failedRoutes)}."
            : "";

        return new
        {
            success = true,
            policy_id = created.PolicyId,
            name = created.DisplayName,
            applied_routes = appliedRoutes,
            failed_routes = failedRoutes,
            message = $"Route policy '{created.DisplayName}' created (ID: {created.PolicyId}).{appliedMsg}{failedMsg}"
        };
    }

    private async Task<object> ExecuteApplyRoutePolicyAsync(JsonElement args)
    {
        var policyId = args.GetProperty("policy_id").GetString()!;
        var routeId = args.GetProperty("route_id").GetString()!;

        var success = await _policyService.ApplyRoutePolicyAsync(policyId, routeId);
        return new
        {
            success,
            policy_id = policyId,
            route_id = routeId,
            message = success
                ? $"Route policy '{policyId}' applied to route '{routeId}'."
                : $"Failed to apply policy '{policyId}' to route '{routeId}'."
        };
    }

    private async Task<object> ExecuteDeletePolicyAsync(JsonElement args)
    {
        var policyId = args.GetProperty("policy_id").GetString()!;
        var type = args.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() : "auto";

        // Try cluster first, then route
        if (type == "cluster" || type == "auto")
        {
            var deleted = await _policyService.DeleteClusterPolicyAsync(policyId);
            if (deleted)
                return new { success = true, policy_id = policyId, type = "cluster", message = $"Cluster policy '{policyId}' deleted." };
        }

        if (type == "route" || type == "auto")
        {
            var deleted = await _policyService.DeleteRoutePolicyAsync(policyId);
            if (deleted)
                return new { success = true, policy_id = policyId, type = "route", message = $"Route policy '{policyId}' deleted." };
        }

        return new { success = false, policy_id = policyId, message = $"Policy '{policyId}' not found." };
    }
}
