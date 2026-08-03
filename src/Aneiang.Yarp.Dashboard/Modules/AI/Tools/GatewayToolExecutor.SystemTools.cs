using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Tools;

public partial class GatewayToolExecutor
{
    // ===================== SYSTEM TOOLS =====================

    private object ExecuteGetProxyLogs(ToolArgs args)
    {
        var count = Math.Clamp(args.GetInt("count", 50), 1, 200);
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

    private async Task<object> ExecuteSearchLogsAsync(ToolArgs args, CancellationToken ct)
    {
        var count = Math.Clamp(args.GetInt("count", 50), 1, 200);
        var minutes = args.GetInt("time_range_minutes", 60);
        var routeId = args.GetString("route_id");
        var statusMin = args.GetNullableInt("status_code_min");
        var statusMax = args.GetNullableInt("status_code_max");
        var keyword = args.GetString("keyword");

        var startTime = DateTime.Now.AddMinutes(-minutes);
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

    private async Task<object> ExecuteGetTrafficStatsAsync(ToolArgs args, CancellationToken ct)
    {
        var minutes = args.GetInt("time_range_minutes", 60);
        var stats = await _logRepo.GetStatsAsync(minutes, ct);
        var startTime = DateTime.Now.AddMinutes(-minutes);
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

    private object ExecuteGetAuditLog(ToolArgs args)
    {
        var count = Math.Clamp(args.GetInt("count", 20), 1, 100);
        var actionFilter = args.GetString("action_filter");

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

        return new { total = result.Count, clusters = result };
    }

    private async Task<object> ExecuteGetTopIssuesAsync(ToolArgs args, CancellationToken ct)
    {
        var minutes = args.GetInt("time_range_minutes", 60);
        var count = Math.Clamp(args.GetInt("count", 5), 1, 20);
        var startTime = DateTime.Now.AddMinutes(-minutes);
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

    // ===================== WRITE SYSTEM TOOLS =====================

    private object ExecuteTogglePlugin(ToolArgs args)
    {
        var pluginId = args.Get("plugin_id");
        var enabled = args.GetBool("enabled");

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

    private object ExecuteClearLogs()
    {
        _logQuery.ClearLogs();
        return new
        {
            success = true,
            message = "All in-memory proxy logs cleared."
        };
    }

    private async Task<object> ExecuteCreateConfigSnapshotAsync(ToolArgs args)
    {
        var description = args.GetString("description") ?? "AI-assistant snapshot";
        var snapshot = await _configPersistence.SaveSnapshotAsync(description, "ai-assistant");
        return new
        {
            success = true,
            version_id = snapshot.VersionId,
            timestamp = snapshot.Timestamp.ToString("O"),
            message = $"Config snapshot created: {snapshot.VersionId}."
        };
    }

    private async Task<object> ExecuteRollbackConfigAsync(ToolArgs args)
    {
        var versionId = args.Get("version_id");
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
}
