using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Tools;

/// <summary>
/// Executes gateway tool calls requested by the AI model.
/// Split across partial class files by domain:
/// RouteTools, ClusterTools, CircuitBreakerTools, SystemTools.
/// </summary>
public partial class GatewayToolExecutor
{
    private readonly IDynamicYarpConfigService _dynamicConfig;
    private readonly IDashboardRouteQueryService _routeQuery;
    private readonly IDashboardClusterQueryService _clusterQuery;
    private readonly IDashboardLogQueryService _logQuery;
    private readonly IProxyLogRepository _logRepo;
    private readonly ICircuitStateStore _circuitStore;
    private readonly IGatewayPluginManager _pluginManager;
    private readonly IPluginConfigurationRepository _pluginBindings;
    private readonly IPluginBindingMutationService _bindingMutations;
    private readonly ConfigChangeAuditLog _auditLog;
    private readonly IDashboardInfoQueryService _infoService;
    private readonly IConfigPersistenceService _configPersistence;
    private readonly IConfigSnapshotScheduler _snapshotScheduler;
    private readonly INotificationRepository _notificationRepo;
    private readonly DeploymentRestartState _restartState;
    private readonly ILogger<GatewayToolExecutor> _logger;

    public GatewayToolExecutor(
        IDynamicYarpConfigService dynamicConfig,
        IDashboardRouteQueryService routeQuery,
        IDashboardClusterQueryService clusterQuery,
        IDashboardLogQueryService logQuery,
        IProxyLogRepository logRepo,
        ICircuitStateStore circuitStore,
        IGatewayPluginManager pluginManager,
        IPluginConfigurationRepository pluginBindings,
        IPluginBindingMutationService bindingMutations,
        ConfigChangeAuditLog auditLog,
        IDashboardInfoQueryService infoService,
        IConfigPersistenceService configPersistence,
        IConfigSnapshotScheduler snapshotScheduler,
        INotificationRepository notificationRepo,
        DeploymentRestartState restartState,
        ILogger<GatewayToolExecutor> logger)
    {
        _dynamicConfig = dynamicConfig;
        _routeQuery = routeQuery;
        _clusterQuery = clusterQuery;
        _logQuery = logQuery;
        _logRepo = logRepo;
        _circuitStore = circuitStore;
        _pluginManager = pluginManager;
        _pluginBindings = pluginBindings;
        _bindingMutations = bindingMutations;
        _auditLog = auditLog;
        _infoService = infoService;
        _configPersistence = configPersistence;
        _snapshotScheduler = snapshotScheduler;
        _notificationRepo = notificationRepo;
        _restartState = restartState;
        _logger = logger;
    }

    /// <summary>
    /// Execute a tool call and return structured result.
    /// </summary>
    public async Task<AIToolResult> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var args = new ToolArgs(argumentsJson);

            object data = toolName switch
            {
                // Read tools
                "get_routes" => ExecuteGetRoutes(),
                "get_clusters" => ExecuteGetClusters(),
                "get_circuit_status" => ExecuteGetCircuitStatus(),
                "get_proxy_logs" => ExecuteGetProxyLogs(args),
                "get_health_summary" => ExecuteGetHealthSummary(),
                "get_plugins" => ExecuteGetPlugins(),
                "search_logs" => await ExecuteSearchLogsAsync(args, ct),
                "get_traffic_stats" => await ExecuteGetTrafficStatsAsync(args, ct),
                "get_audit_log" => ExecuteGetAuditLog(args),
                "get_gateway_info" => ExecuteGetGatewayInfo(),
                "get_deployment_info" => ExecuteGetDeploymentInfo(),
                "get_alert_summary" => await ExecuteGetAlertSummaryAsync(ct),
                "get_health_check_config" => ExecuteGetHealthCheckConfig(),
                "get_top_issues" => await ExecuteGetTopIssuesAsync(args, ct),
                "export_config" => await ExecuteExportConfigAsync(),
                "get_config_history" => await ExecuteGetConfigHistoryAsync(),
                "get_notification_summary" => await ExecuteGetNotificationSummaryAsync(ct),

                // Write tools
                "create_route" => await ExecuteCreateRouteAsync(args, ct),
                "delete_route" => await ExecuteDeleteRouteAsync(args, ct),
                "create_cluster" => await ExecuteCreateClusterAsync(args, ct),
                "update_cluster" => await ExecuteUpdateClusterAsync(args, ct),
                "delete_cluster" => await ExecuteDeleteClusterAsync(args, ct),
                "create_circuit_breaker" => await ExecuteCreateCircuitBreakerAsync(args),
                "reset_circuit_breaker" => ExecuteResetCircuitBreaker(args),
                "toggle_plugin" => ExecuteTogglePlugin(args),
                "rename_route" => await ExecuteRenameRouteAsync(args),
                "rename_cluster" => await ExecuteRenameClusterAsync(args),
                "clear_logs" => ExecuteClearLogs(),
                "create_config_snapshot" => await ExecuteCreateConfigSnapshotAsync(args),
                "rollback_config" => await ExecuteRollbackConfigAsync(args),

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
}
