using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Client-side internationalization dictionary for the Dashboard UI.
/// Provides zh-CN and en-US translations as a flat key-value map
/// that is serialized to JSON and consumed by the browser.
/// </summary>
public static class DashboardI18n
{
    /// <summary>Chinese (Simplified) translation dictionary.</summary>
    public static readonly Dictionary<string, string> ZhCN = new()
    {
        // ── Layout ──
        ["layout.title"] = "网关维护仪表盘",
        ["layout.brand"] = "网关维护仪表盘",
        ["layout.section.monitor"] = "监控",
        ["layout.tab.overview"] = "总览",
        ["layout.tab.services"] = "服务集群",
        ["layout.tab.routes"] = "路由配置",
        ["layout.section.diag"] = "诊断",
        ["layout.tab.logs"] = "日志",
        ["layout.lang.switch"] = "English",
        ["layout.lang.tooltip"] = "Switch to English",

        // ── Login ──
        ["login.title"] = "登录 - 网关仪表盘",
        ["login.brand"] = "网关仪表盘",
        ["login.username"] = "用户名",
        ["login.username.placeholder"] = "请输入用户名",
        ["login.password"] = "密码",
        ["login.password.placeholder"] = "请输入密码",
        ["login.submit"] = "登 录",
        ["login.submitting"] = "登录中...",
        ["login.failed"] = "登录失败",
        ["login.networkError"] = "网络错误，请重试",

        // ── Index – stat bar ──
        ["index.title"] = "总览",
        ["index.label.version"] = "版本：",
        ["index.label.env"] = "环境：",
        ["index.label.start"] = "启动：",
        ["index.label.uptime"] = "运行：",
        ["index.label.memory"] = "内存：",
        ["index.label.host"] = "主机：",
        ["index.btn.refresh"] = "刷新",
        ["index.stat.clusters"] = "服务集群",
        ["index.stat.health"] = "健康 / 未知 / 异常",
        ["index.stat.routes"] = "路由规则",
        ["index.loading"] = "加载中...",

        // ── Index – cluster panel ──
        ["index.cluster.title"] = "集群 & 目标节点状态",
        ["index.cluster.th.name"] = "集群名称",
        ["index.cluster.th.dest"] = "节点名",
        ["index.cluster.th.address"] = "目标地址",
        ["index.cluster.th.active"] = "主动健康",
        ["index.cluster.th.passive"] = "被动健康",
        ["index.cluster.th.policy"] = "均衡策略",
        ["index.cluster.empty"] = "暂无集群数据",
        ["index.cluster.updated"] = "更新于 ",

        // ── Index – route panel ──
        ["index.route.title"] = "路由规则列表",
        ["index.route.th.order"] = "优先级",
        ["index.route.th.name"] = "路由名称",
        ["index.route.th.path"] = "匹配路径",
        ["index.route.th.cluster"] = "关联集群",
        ["index.route.th.methods"] = "允许方法",
        ["index.route.empty"] = "暂无路由数据",
        ["index.route.updated"] = "更新于 ",
        ["index.route.allMethods"] = "All",

        // ── Index – log panel ──
        ["index.log.title"] = "YARP 实时日志",
        ["index.log.gatewayOnly"] = "仅请求日志",
        ["index.log.showing"] = "显示 ",
        ["index.log.of"] = " / ",
        ["index.log.entries"] = " 条",
        ["index.log.startListen"] = "开启监听",
        ["index.log.stopListen"] = "监听中",
        ["index.log.clear"] = "清空",
        ["index.log.empty"] = "暂无日志",
        ["index.log.updated"] = "更新于 ",

        // ── Index – log detail ──
        ["index.log.category"] = "Category:",
        ["index.log.message"] = "Message:",
        ["index.log.details"] = "Details:",
        ["index.log.exception"] = "Exception:",

        // ── Index – health badges ──
        ["index.health.healthy"] = "Healthy",
        ["index.health.unhealthy"] = "Unhealthy",
        ["index.health.unknown"] = "Unknown",

        // ── Index – cluster detail labels ──
        ["index.detail.destinations"] = "Destinations",
        ["index.detail.name"] = "Name",
        ["index.detail.address"] = "Address",
        ["index.detail.healthUrl"] = "Health URL",
        ["index.detail.host"] = "Host",
        ["index.detail.active"] = "Active",
        ["index.detail.passive"] = "Passive",
        ["index.detail.metadata"] = "Metadata",
        ["index.detail.key"] = "Key",
        ["index.detail.value"] = "Value",
        ["index.detail.sessionAffinity"] = "SessionAffinity:",
        ["index.detail.healthCheck"] = "HealthCheck:",
        ["index.detail.httpClient"] = "HttpClient:",
        ["index.detail.httpRequest"] = "HttpRequest:",
        ["index.detail.clusterMetadata"] = "Cluster Metadata:",
        ["index.detail.property"] = "Property",
        ["index.detail.forwardTo"] = "Forward to:",
        ["index.detail.transforms"] = "Transforms",
        ["index.detail.headers"] = "Headers",
        ["index.detail.queryParams"] = "Query Parameters",
        ["index.detail.values"] = "Values",
        ["index.detail.mode"] = "Mode",

        // ── JS error messages ──
        ["index.js.loadInfoFailed"] = "加载网关信息失败",
        ["index.js.loadClusterFailed"] = "加载集群数据失败",
        ["index.js.loadRouteFailed"] = "加载路由数据失败",
    };

    /// <summary>English translation dictionary.</summary>
    public static readonly Dictionary<string, string> EnUS = new()
    {
        // ── Layout ──
        ["layout.title"] = "Gateway Dashboard",
        ["layout.brand"] = "Gateway Dashboard",
        ["layout.section.monitor"] = "Monitor",
        ["layout.tab.overview"] = "Overview",
        ["layout.tab.services"] = "Clusters",
        ["layout.tab.routes"] = "Routes",
        ["layout.section.diag"] = "Diagnostics",
        ["layout.tab.logs"] = "Logs",
        ["layout.lang.switch"] = "中文",
        ["layout.lang.tooltip"] = "切换到中文",

        // ── Login ──
        ["login.title"] = "Login - Gateway Dashboard",
        ["login.brand"] = "Gateway Dashboard",
        ["login.username"] = "Username",
        ["login.username.placeholder"] = "Enter username",
        ["login.password"] = "Password",
        ["login.password.placeholder"] = "Enter password",
        ["login.submit"] = "Login",
        ["login.submitting"] = "Logging in...",
        ["login.failed"] = "Login failed",
        ["login.networkError"] = "Network error, please retry",

        // ── Index – stat bar ──
        ["index.title"] = "Overview",
        ["index.label.version"] = "Version:",
        ["index.label.env"] = "Env:",
        ["index.label.start"] = "Started:",
        ["index.label.uptime"] = "Uptime:",
        ["index.label.memory"] = "Memory:",
        ["index.label.host"] = "Host:",
        ["index.btn.refresh"] = "Refresh",
        ["index.stat.clusters"] = "Clusters",
        ["index.stat.health"] = "Healthy / Unknown / Unhealthy",
        ["index.stat.routes"] = "Routes",
        ["index.loading"] = "Loading...",

        // ── Index – cluster panel ──
        ["index.cluster.title"] = "Cluster & Destination Status",
        ["index.cluster.th.name"] = "Cluster",
        ["index.cluster.th.dest"] = "Destination",
        ["index.cluster.th.address"] = "Address",
        ["index.cluster.th.active"] = "Active Health",
        ["index.cluster.th.passive"] = "Passive Health",
        ["index.cluster.th.policy"] = "Policy",
        ["index.cluster.empty"] = "No cluster data",
        ["index.cluster.updated"] = "Updated at ",

        // ── Index – route panel ──
        ["index.route.title"] = "Route Rules",
        ["index.route.th.order"] = "Priority",
        ["index.route.th.name"] = "Route",
        ["index.route.th.path"] = "Path",
        ["index.route.th.cluster"] = "Cluster",
        ["index.route.th.methods"] = "Methods",
        ["index.route.empty"] = "No route data",
        ["index.route.updated"] = "Updated at ",
        ["index.route.allMethods"] = "All",

        // ── Index – log panel ──
        ["index.log.title"] = "YARP Live Logs",
        ["index.log.gatewayOnly"] = "Gateway only",
        ["index.log.showing"] = "Showing ",
        ["index.log.of"] = " / ",
        ["index.log.entries"] = " entries",
        ["index.log.startListen"] = "Start",
        ["index.log.stopListen"] = "Listening",
        ["index.log.clear"] = "Clear",
        ["index.log.empty"] = "No logs",
        ["index.log.updated"] = "Updated at ",

        // ── Index – log detail ──
        ["index.log.category"] = "Category:",
        ["index.log.message"] = "Message:",
        ["index.log.details"] = "Details:",
        ["index.log.exception"] = "Exception:",

        // ── Index – health badges ──
        ["index.health.healthy"] = "Healthy",
        ["index.health.unhealthy"] = "Unhealthy",
        ["index.health.unknown"] = "Unknown",

        // ── Index – cluster detail labels ──
        ["index.detail.destinations"] = "Destinations",
        ["index.detail.name"] = "Name",
        ["index.detail.address"] = "Address",
        ["index.detail.healthUrl"] = "Health URL",
        ["index.detail.host"] = "Host",
        ["index.detail.active"] = "Active",
        ["index.detail.passive"] = "Passive",
        ["index.detail.metadata"] = "Metadata",
        ["index.detail.key"] = "Key",
        ["index.detail.value"] = "Value",
        ["index.detail.sessionAffinity"] = "SessionAffinity:",
        ["index.detail.healthCheck"] = "HealthCheck:",
        ["index.detail.httpClient"] = "HttpClient:",
        ["index.detail.httpRequest"] = "HttpRequest:",
        ["index.detail.clusterMetadata"] = "Cluster Metadata:",
        ["index.detail.property"] = "Property",
        ["index.detail.forwardTo"] = "Forward to:",
        ["index.detail.transforms"] = "Transforms",
        ["index.detail.headers"] = "Headers",
        ["index.detail.queryParams"] = "Query Parameters",
        ["index.detail.values"] = "Values",
        ["index.detail.mode"] = "Mode",

        // ── JS error messages ──
        ["index.js.loadInfoFailed"] = "Failed to load gateway info",
        ["index.js.loadClusterFailed"] = "Failed to load cluster data",
        ["index.js.loadRouteFailed"] = "Failed to load route data",
    };

    /// <summary>Returns the translation dictionary for the given locale.</summary>
    public static Dictionary<string, string> GetDict(string? locale)
    {
        return string.Equals(locale, "en-US", StringComparison.OrdinalIgnoreCase)
            ? EnUS
            : ZhCN;
    }

    /// <summary>Serializes the full translation dictionary to JSON for client-side consumption.</summary>
    public static string AllAsJson(string? locale)
    {
        var dict = GetDict(locale);
        return JsonSerializer.Serialize(dict);
    }
}
