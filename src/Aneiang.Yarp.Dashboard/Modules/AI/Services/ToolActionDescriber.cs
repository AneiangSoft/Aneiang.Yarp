using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Services;

/// <summary>
/// Generates human-readable descriptions for AI pending actions (confirmation cards).
/// Supports bilingual output (Chinese/English) based on locale.
/// </summary>
public static class ToolActionDescriber
{
    /// <summary>Build a human-readable description of what a pending write tool will do.</summary>
    public static string Describe(string toolName, Dictionary<string, JsonElement> args, bool isChinese)
    {
        if (isChinese)
        {
            return toolName switch
            {
                "create_route" => $"创建路由 '{Arg(args, "route_name")}'，路径 '{Arg(args, "path")}' -> 集群 '{Arg(args, "cluster_id")}'",
                "delete_route" => $"删除路由 '{Arg(args, "route_id")}'",
                "create_cluster" => $"创建集群 '{Arg(args, "cluster_id")}'",
                "update_cluster" => $"更新集群 '{Arg(args, "cluster_id")}'",
                "delete_cluster" => $"删除集群 '{Arg(args, "cluster_id")}'",
                "create_circuit_breaker" => $"为集群 '{Arg(args, "cluster_id")}' 配置熔断器策略",
                "reset_circuit_breaker" => args.ContainsKey("cluster_id")
                    ? $"重置集群 '{Arg(args, "cluster_id")}' 的熔断器"
                    : "重置所有熔断器",
                "toggle_plugin" => $"{(Arg(args, "enabled") == "true" ? "启用" : "禁用")}插件 '{Arg(args, "plugin_id")}'",
                "update_waf_settings" => "更新 WAF 安全设置",
                "rename_route" => $"重命名路由 '{Arg(args, "old_route_id")}' → '{Arg(args, "new_route_id")}'",
                "rename_cluster" => $"重命名集群 '{Arg(args, "old_cluster_id")}' → '{Arg(args, "new_cluster_id")}'",
                "clear_logs" => "清空内存中的代理日志",
                "create_config_snapshot" => $"创建配置快照: {Arg(args, "description")}",
                "rollback_config" => $"回滚配置到版本 '{Arg(args, "version_id")}'",
                "create_cluster_policy" => ArgArray(args, "cluster_ids") is string cids
                    ? $"创建集群策略 '{Arg(args, "name")}' 并应用到 [{cids}]"
                    : $"创建集群策略模板 '{Arg(args, "name")}'",
                "apply_cluster_policy" => $"将集群策略 '{Arg(args, "policy_id")}' 应用到集群 '{Arg(args, "cluster_id")}'",
                "create_route_policy" => ArgArray(args, "route_ids") is string rids
                    ? $"创建路由策略 '{Arg(args, "name")}' 并应用到 [{rids}]"
                    : $"创建路由策略模板 '{Arg(args, "name")}'",
                "apply_route_policy" => $"将路由策略 '{Arg(args, "policy_id")}' 应用到路由 '{Arg(args, "route_id")}'",
                "delete_policy" => $"删除策略 '{Arg(args, "policy_id")}'",
                _ => $"执行 {toolName}"
            };
        }

        return toolName switch
        {
            "create_route" => $"Create route '{Arg(args, "route_name")}' with path '{Arg(args, "path")}' -> cluster '{Arg(args, "cluster_id")}'",
            "delete_route" => $"Delete route '{Arg(args, "route_id")}'",
            "create_cluster" => $"Create cluster '{Arg(args, "cluster_id")}'",
            "update_cluster" => $"Update cluster '{Arg(args, "cluster_id")}'",
            "delete_cluster" => $"Delete cluster '{Arg(args, "cluster_id")}'",
            "create_circuit_breaker" => $"Create circuit breaker policy for cluster '{Arg(args, "cluster_id")}'",
            "reset_circuit_breaker" => args.ContainsKey("cluster_id")
                ? $"Reset circuit breaker for cluster '{Arg(args, "cluster_id")}'"
                : "Reset all circuit breakers",
            "toggle_plugin" => $"{(Arg(args, "enabled") == "true" ? "Enable" : "Disable")} plugin '{Arg(args, "plugin_id")}'",
            "update_waf_settings" => "Update WAF settings",
            "rename_route" => $"Rename route '{Arg(args, "old_route_id")}' → '{Arg(args, "new_route_id")}'",
            "rename_cluster" => $"Rename cluster '{Arg(args, "old_cluster_id")}' → '{Arg(args, "new_cluster_id")}'",
            "clear_logs" => "Clear in-memory proxy logs",
            "create_config_snapshot" => $"Create config snapshot: {Arg(args, "description")}",
            "rollback_config" => $"Rollback config to version '{Arg(args, "version_id")}'",
            "create_cluster_policy" => ArgArray(args, "cluster_ids") is string cids
                ? $"Create cluster policy '{Arg(args, "name")}' and apply to [{cids}]"
                : $"Create cluster policy template '{Arg(args, "name")}'",
            "apply_cluster_policy" => $"Apply cluster policy '{Arg(args, "policy_id")}' to cluster '{Arg(args, "cluster_id")}'",
            "create_route_policy" => ArgArray(args, "route_ids") is string rids
                ? $"Create route policy '{Arg(args, "name")}' and apply to [{rids}]"
                : $"Create route policy template '{Arg(args, "name")}'",
            "apply_route_policy" => $"Apply route policy '{Arg(args, "policy_id")}' to route '{Arg(args, "route_id")}'",
            "delete_policy" => $"Delete policy '{Arg(args, "policy_id")}'",
            _ => $"Execute {toolName}"
        };
    }

    private static string Arg(Dictionary<string, JsonElement> args, string key)
    {
        if (args.TryGetValue(key, out var val))
            return val.ValueKind == JsonValueKind.String ? val.GetString() ?? "" : val.ToString();
        return "";
    }

    private static string? ArgArray(Dictionary<string, JsonElement> args, string key)
    {
        if (args.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.Array)
        {
            var items = val.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList();
            return items.Count > 0 ? string.Join(", ", items) : null;
        }
        return null;
    }
}
