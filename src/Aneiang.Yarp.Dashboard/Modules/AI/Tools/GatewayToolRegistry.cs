namespace Aneiang.Yarp.Dashboard.Modules.AI.Tools;

/// <summary>
/// Registry of all gateway tools available for AI function calling.
/// Each tool has a JSON Schema describing its parameters.
/// Read-only tools are auto-executed; write tools require user confirmation.
/// </summary>
public class GatewayToolRegistry
{
    private static readonly List<AIToolDefinition> _tools = BuildTools();

    /// <summary>Get all tool definitions in OpenAI function-calling format.</summary>
    public List<AIToolDefinition> GetToolDefinitions() => _tools;

    /// <summary>Whether a tool is read-only (safe to auto-execute).</summary>
    public bool IsReadOnlyTool(string name) =>
        _tools.FirstOrDefault(t => t.Name == name)?.IsReadOnly ?? false;

    // ───────────── Builder Helpers ─────────────

    private static AIToolDefinition R(string name, string desc, object? props = null, string[]? req = null) =>
        new() { Name = name, Description = desc, IsReadOnly = true, Parameters = P(props, req) };

    private static AIToolDefinition W(string name, string desc, object? props = null, string[]? req = null) =>
        new() { Name = name, Description = desc, IsReadOnly = false, Parameters = P(props, req) };

    private static object P(object? props, string[]? req) =>
        new { type = "object", properties = props ?? new { }, required = req ?? Array.Empty<string>() };

    private static readonly string[] None = Array.Empty<string>();

    private static List<AIToolDefinition> BuildTools()
    {
        return
        [
            // ===================== READ TOOLS =====================

            R("get_routes", "Get all configured proxy routes with their match rules, cluster bindings, and status."),

            R("get_clusters", "Get all clusters with their destinations, health status, and load balancing policy."),

            R("get_circuit_status", "Get circuit breaker status for all clusters. Shows open/closed/half-open states, failure counts, and recovery info."),

            R("get_proxy_logs", "Get recent proxy request logs with status codes, latency, route info. Use count to limit results.",
                new { count = new { type = "integer", description = "Number of recent log entries to return (default 50, max 200)." } }),

            R("get_health_summary", "Get overall health summary of all destinations across all clusters."),

            R("get_plugins", "Get all installed plugins and their enabled/disabled status."),

            R("get_waf_settings", "Get current WAF (Web Application Firewall) settings including enabled rules, IP lists, and size limits."),

            R("search_logs", "Search proxy logs with filters. Use this to find specific errors, requests by route, status codes, or keywords within a time range.",
                new
                {
                    route_id = new { type = "string", description = "Filter by route ID." },
                    status_code_min = new { type = "integer", description = "Minimum status code (e.g. 400 for client errors)." },
                    status_code_max = new { type = "integer", description = "Maximum status code (e.g. 599 for server errors)." },
                    keyword = new { type = "string", description = "Search keyword in path or response." },
                    time_range_minutes = new { type = "integer", description = "Look back N minutes (default 60)." },
                    count = new { type = "integer", description = "Max results to return (default 50)." }
                }),

            R("get_traffic_stats", "Get traffic statistics: total requests, success/error rate, latency percentiles (P50/P90/P99), top error routes, and 5xx alert count.",
                new { time_range_minutes = new { type = "integer", description = "Time range in minutes (default 60)." } }),

            R("get_rate_limit_status", "Get current rate limiting configuration and runtime status (active limiter count)."),

            R("get_retry_config", "Get current request retry configuration (enabled, max retries, backoff delay)."),

            R("get_audit_log", "Get recent configuration change audit log entries. Shows who changed what and when.",
                new
                {
                    count = new { type = "integer", description = "Number of entries to return (default 20)." },
                    action_filter = new { type = "string", description = "Filter by action type, e.g. 'AddRoute', 'RemoveCluster'." }
                }),

            // ===================== EXTENDED READ TOOLS =====================

            R("get_gateway_info", "Get gateway basic information: version, uptime, memory usage, CPU usage, machine name, thread count, and GC count."),

            R("get_deployment_info", "Get deployment status: whether a restart is required, restart reasons, and runtime circuit breaker state count."),

            R("get_alert_summary", "Get alert summary: unhealthy destination count, open circuit count, recent 5xx errors, and whether any alerts are active."),

            R("get_health_check_config", "Get health check configuration for all clusters that have health checks configured (active/passive settings, endpoints, policies)."),

            R("get_top_issues", "Get top problematic routes: routes with highest error counts and request volumes in a time range.",
                new
                {
                    time_range_minutes = new { type = "integer", description = "Time range in minutes (default 60)." },
                    count = new { type = "integer", description = "Number of top issues to return (default 5, max 20)." }
                }),

            R("export_config", "Export the complete YARP gateway configuration (all routes and clusters) as JSON."),

            R("get_config_history", "Get configuration change history: list of all configuration snapshots with version ID, timestamp, and description."),

            R("get_notification_summary", "Get notification system summary: channel count, rule count, enabled status, and recent notification history."),

            R("get_policies", "Get all gateway policies: route policies (retry, rate-limit, WAF) and cluster policies (circuit breaker). Shows which routes/clusters each policy is applied to."),

            // ===================== WRITE TOOLS =====================

            W("create_route", "Create or update a proxy route. Call this tool immediately when the user asks to add/create a route — do not just describe what you plan to do. Specify route name, YARP path pattern, and target cluster. Creates the cluster if it does not exist.",
                new
                {
                    route_name = new { type = "string", description = "Unique route identifier." },
                    path = new { type = "string", description = "YARP path pattern. Use format like '/prefix/{{**catch-all}}' for wildcard matching. Examples: '/api/v1/{{**catch-all}}', '/bilibili/{{**catch-all}}'." },
                    cluster_id = new { type = "string", description = "Target cluster ID to forward requests to." },
                    destination_address = new { type = "string", description = "Backend destination URL, e.g. 'http://10.0.0.1:8080'." },
                    methods = new { type = "array", items = new { type = "string" }, description = "Optional: HTTP methods to match (GET, POST, etc.). Empty means all methods." }
                },
                req: new[] { "route_name", "path", "cluster_id", "destination_address" }),

            W("delete_route", "Delete a proxy route by its route ID. Optionally removes the associated cluster if no other routes reference it.",
                new { route_id = new { type = "string", description = "The route ID to delete." } },
                req: new[] { "route_id" }),

            W("create_cluster", "Create a new cluster with one or more backend destinations.",
                new
                {
                    cluster_id = new { type = "string", description = "Unique cluster identifier." },
                    destinations = new { type = "object", description = "Map of destination name to URL address, e.g. {\"node1\": \"http://10.0.0.1:8080\"}." },
                    load_balancing = new { type = "string", description = "Load balancing policy: RoundRobin, LeastRequests, PowerOfTwoChoices, Random.", @enum = new[] { "RoundRobin", "LeastRequests", "PowerOfTwoChoices", "Random" } }
                },
                req: new[] { "cluster_id", "destinations" }),

            W("update_cluster", "Update an existing cluster: add/remove destinations or change load balancing policy.",
                new
                {
                    cluster_id = new { type = "string", description = "Cluster ID to update." },
                    destinations = new { type = "object", description = "New destination map (replaces existing). Map of name to URL." },
                    load_balancing = new { type = "string", description = "New load balancing policy." }
                },
                req: new[] { "cluster_id" }),

            W("delete_cluster", "Delete a cluster. Fails if routes still reference it — delete those routes first.",
                new { cluster_id = new { type = "string", description = "Cluster ID to delete." } },
                req: new[] { "cluster_id" }),

            W("create_circuit_breaker", "Create or update a circuit breaker policy for a specific cluster. Call this tool immediately when the user asks to configure/create a circuit breaker — do not just describe what you plan to do. The circuit-breaker plugin must be enabled for this to take effect.",
                new
                {
                    cluster_id = new { type = "string", description = "Target cluster ID to apply the circuit breaker policy to." },
                    failure_threshold = new { type = "integer", description = "Consecutive failures before opening the circuit. Default: 5." },
                    recovery_timeout_seconds = new { type = "integer", description = "Seconds to wait before attempting recovery (half-open). Default: 30." },
                    half_open_max_attempts = new { type = "integer", description = "Max requests allowed in half-open state before closing. Default: 1." },
                    failure_status_codes = new { type = "array", items = new { type = "integer" }, description = "HTTP status codes that count as failures. Default: [500, 502, 503, 504]." },
                    enabled = new { type = "boolean", description = "Enable the circuit breaker. Set to false to remove/disable. Default: true." }
                },
                req: new[] { "cluster_id" }),

            W("reset_circuit_breaker", "Reset all circuit breakers to Closed state, or reset a specific cluster's circuit breaker.",
                new { cluster_id = new { type = "string", description = "Optional: specific cluster ID to reset. Omit to reset all." } }),

            W("toggle_plugin", "Enable or disable a gateway plugin (circuit-breaker, waf, rate-limit, request-retry, ai).",
                new
                {
                    plugin_id = new { type = "string", description = "Plugin ID to toggle.", @enum = new[] { "circuit-breaker", "waf", "rate-limit", "request-retry", "ai" } },
                    enabled = new { type = "boolean", description = "True to enable, false to disable." }
                },
                req: new[] { "plugin_id", "enabled" }),

            W("update_waf_settings", "Update WAF (Web Application Firewall) settings. Supports toggling rules, IP lists, and size limits.",
                new
                {
                    enabled = new { type = "boolean", description = "Enable or disable WAF globally." },
                    enable_sql_injection = new { type = "boolean", description = "Enable SQL injection detection." },
                    enable_xss = new { type = "boolean", description = "Enable XSS detection." },
                    enable_path_traversal = new { type = "boolean", description = "Enable path traversal detection." },
                    ip_blacklist = new { type = "array", items = new { type = "string" }, description = "IP addresses to block." }
                }),

            // ===================== EXTENDED WRITE TOOLS =====================

            W("rename_route", "Rename an existing route. Atomically updates the route ID and all references.",
                new
                {
                    old_route_id = new { type = "string", description = "Current route ID." },
                    new_route_id = new { type = "string", description = "New route ID." }
                },
                req: new[] { "old_route_id", "new_route_id" }),

            W("rename_cluster", "Rename an existing cluster. Atomically updates the cluster ID and all referencing routes.",
                new
                {
                    old_cluster_id = new { type = "string", description = "Current cluster ID." },
                    new_cluster_id = new { type = "string", description = "New cluster ID." }
                },
                req: new[] { "old_cluster_id", "new_cluster_id" }),

            W("clear_logs", "Clear all in-memory proxy logs. Historical logs in SQLite are not affected."),

            W("create_config_snapshot", "Create a manual configuration snapshot (backup) before making changes. Use this before risky operations.",
                new { description = new { type = "string", description = "Optional description for the snapshot." } }),

            W("rollback_config", "Rollback gateway configuration to a previous snapshot version. Use get_config_history first to find the version ID.",
                new { version_id = new { type = "string", description = "The snapshot version ID to rollback to." } },
                req: new[] { "version_id" }),

            // ===================== POLICY TOOLS =====================

            W("create_cluster_policy", "Create a cluster policy with circuit breaker settings and optionally apply it to clusters in one step. If cluster_ids is provided, the policy is created and applied to those clusters immediately.",
                new
                {
                    name = new { type = "string", description = "Display name for the policy." },
                    policy_id = new { type = "string", description = "Optional: custom policy ID. Auto-generated if omitted." },
                    description = new { type = "string", description = "Optional description." },
                    failure_threshold = new { type = "integer", description = "Consecutive failures before opening circuit. Default: 5." },
                    recovery_timeout_seconds = new { type = "integer", description = "Seconds before recovery attempt. Default: 30." },
                    half_open_max_attempts = new { type = "integer", description = "Max requests in half-open state. Default: 1." },
                    failure_status_codes = new { type = "array", items = new { type = "integer" }, description = "Failure status codes. Default: [500,502,503,504]." },
                    cluster_ids = new { type = "array", items = new { type = "string" }, description = "Optional: cluster IDs to apply this policy to immediately after creation." }
                },
                req: new[] { "name" }),

            W("apply_cluster_policy", "Apply an existing cluster policy to a specific cluster. The policy's circuit breaker settings will be configured on the cluster.",
                new
                {
                    policy_id = new { type = "string", description = "Cluster policy ID to apply." },
                    cluster_id = new { type = "string", description = "Target cluster ID." }
                },
                req: new[] { "policy_id", "cluster_id" }),

            W("create_route_policy", "Create a route policy with optional retry and rate-limit settings, and optionally apply it to routes in one step. If route_ids is provided, the policy is created and applied to those routes immediately.",
                new
                {
                    name = new { type = "string", description = "Display name for the policy." },
                    policy_id = new { type = "string", description = "Optional: custom policy ID. Auto-generated if omitted." },
                    description = new { type = "string", description = "Optional description." },
                    retry_enabled = new { type = "boolean", description = "Enable retry for this policy. Default: false." },
                    max_retries = new { type = "integer", description = "Max retry attempts (when retry enabled). Default: 3." },
                    rate_limit_enabled = new { type = "boolean", description = "Enable rate limiting for this policy. Default: false." },
                    permit_limit = new { type = "integer", description = "Requests per window (when rate limit enabled). Default: 100." },
                    window = new { type = "string", description = "Rate limit window duration. Default: '1m'." },
                    route_ids = new { type = "array", items = new { type = "string" }, description = "Optional: route IDs to apply this policy to immediately after creation." }
                },
                req: new[] { "name" }),

            W("apply_route_policy", "Apply an existing route policy to a specific route. The policy's retry/rate-limit/WAF settings will be configured via route metadata.",
                new
                {
                    policy_id = new { type = "string", description = "Route policy ID to apply." },
                    route_id = new { type = "string", description = "Target route ID." }
                },
                req: new[] { "policy_id", "route_id" }),

            W("delete_policy", "Delete a gateway policy (route or cluster). Automatically unapplies from all targets before deletion.",
                new
                {
                    policy_id = new { type = "string", description = "Policy ID to delete." },
                    type = new { type = "string", description = "Policy type: 'route', 'cluster', or 'auto' (try both). Default: 'auto'.", @enum = new[] { "route", "cluster", "auto" } }
                },
                req: new[] { "policy_id" })
        ];
    }
}
