using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Modules.Webhook.Services;

/// <summary>
/// DingTalk (钉钉) custom robot webhook provider.
/// Converts the generic payload into DingTalk's <c>markdown</c> message format
/// for rich notification display, and handles DingTalk-specific HMAC-SHA256 signing.
/// </summary>
/// <remarks>
/// DingTalk webhook URL format: https://oapi.dingtalk.com/robot/send?access_token=XXX
/// With signing, appends &amp;timestamp=XXX&amp;sign=XXX to the URL.
///
/// DingTalk signature algorithm:
///   stringToSign = timestamp + "\n" + secret
///   sign = Base64(HmacSHA256(secret, stringToSign))
///   sign = UrlEncode(sign)
/// </remarks>
public class DingTalkWebhookProvider : IWebhookProvider
{
    public string[] SupportedHosts { get; } = ["oapi.dingtalk.com"];

    public string PlatformName { get; } = "dingtalk";

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// Human-readable event type labels (Chinese).
    /// </summary>
    private static readonly Dictionary<string, string> _eventLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AddRoute"] = "➕ 路由新增",
        ["UpdateRoute"] = "✏️ 路由更新",
        ["RemoveRoute"] = "🗑️ 路由删除",
        ["AddCluster"] = "➕ 集群新增",
        ["UpdateCluster"] = "✏️ 集群更新",
        ["RemoveCluster"] = "🗑️ 集群删除",
        ["RenameCluster"] = "🔄 集群重命名",
        ["RollbackConfig"] = "⏪ 配置回滚",
        ["test"] = "🔧 测试推送"
    };

    /// <summary>
    /// Emoji indicators for event severity.
    /// </summary>
    private static readonly Dictionary<string, string> _eventEmojis = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AddRoute"] = "🟢",
        ["UpdateRoute"] = "🟡",
        ["RemoveRoute"] = "🔴",
        ["AddCluster"] = "🟢",
        ["UpdateCluster"] = "🟡",
        ["RemoveCluster"] = "🔴",
        ["RenameCluster"] = "🔵",
        ["RollbackConfig"] = "🟠",
        ["test"] = "⚪"
    };

    public WebhookRequest BuildRequest(string url, WebhookPayload payload, string? secret)
    {
        var markdownContent = FormatMarkdown(payload);
        var eventName = payload.EventLabel ?? _eventLabels.GetValueOrDefault(payload.EventType, payload.EventType);
        var title = $"网关变更: {eventName}";

        var body = JsonSerializer.Serialize(new
        {
            msgtype = "markdown",
            markdown = new { title, text = markdownContent }
        }, _jsonOptions);

        var request = new WebhookRequest
        {
            Url = url,
            Body = body
        };

        // DingTalk signing: append timestamp & sign to URL
        if (!string.IsNullOrEmpty(secret))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var stringToSign = $"{timestamp}\n{secret}";
            var sign = ComputeDingTalkSign(stringToSign, secret);
            var separator = url.Contains('?') ? "&" : "?";
            request.Url = $"{url}{separator}timestamp={timestamp}&sign={sign}";
        }

        return request;
    }

    /// <summary>
    /// Build DingTalk markdown-formatted content.
    /// </summary>
    private static string FormatMarkdown(WebhookPayload payload)
    {
        var sb = new StringBuilder();
        var emoji = _eventEmojis.GetValueOrDefault(payload.EventType, "⚡");
        var eventLabel = payload.EventLabel ?? _eventLabels.GetValueOrDefault(payload.EventType, payload.EventType);
        var gatewayName = !string.IsNullOrEmpty(payload.GatewayName) ? payload.GatewayName : "YARP Gateway";

        // Header
        sb.AppendLine($"### {emoji} {gatewayName} 配置变更通知");
        sb.AppendLine();

        // Event summary line
        sb.AppendLine($"**{eventLabel}** `{payload.Target}`");
        sb.AppendLine();

        // Metadata section
        sb.AppendLine("---");
        sb.AppendLine($"- **事件类型**: {eventLabel}");
        sb.AppendLine($"- **操作对象**: `{payload.Target}`");
        sb.AppendLine($"- **发生时间**: {payload.Timestamp:yyyy-MM-dd HH:mm:ss}");

        if (!string.IsNullOrEmpty(payload.Operator))
            sb.AppendLine($"- **操作人**: {payload.Operator}");

        sb.AppendLine();

        // Details section - parse and format based on event type
        if (payload.Details != null)
        {
            sb.AppendLine("---");
            sb.AppendLine(FormatDetails(payload.EventType, payload.Details));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Format event-specific details into readable markdown.
    /// </summary>
    private static string FormatDetails(string eventType, object details)
    {
        // Serialize to JsonElement for dynamic inspection
        var json = JsonSerializer.Serialize(details, _jsonOptions);
        var elem = JsonSerializer.Deserialize<JsonElement>(json);

        return eventType.ToUpperInvariant() switch
        {
            "ADDROUTE" or "UPDATEROUTE" => FormatRouteDetails(elem, isNew: eventType.Equals("AddRoute", StringComparison.OrdinalIgnoreCase)),
            "REMOVEROUTE" => FormatRemoveRouteDetails(elem),
            "ADDCLUSTER" or "UPDATECLUSTER" => FormatClusterDetails(elem, isNew: eventType.Equals("AddCluster", StringComparison.OrdinalIgnoreCase)),
            "REMOVECLUSTER" => FormatRemoveClusterDetails(elem),
            "RENAMECLUSTER" => FormatRenameClusterDetails(elem),
            "ROLLBACKCONFIG" => FormatRollbackDetails(elem),
            "TEST" => FormatTestDetails(elem),
            _ => FormatGenericDetails(elem)
        };
    }

    private static string FormatRouteDetails(JsonElement elem, bool isNew)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"#### {(isNew ? "新增路由配置" : "更新路由配置")}");

        if (TryGet(elem, "clusterId", out var clusterId))
            sb.AppendLine($"- **集群**: `{clusterId}`");

        if (TryGet(elem, "matchPath", out var matchPath))
            sb.AppendLine($"- **匹配路径**: `{matchPath}`");

        if (TryGet(elem, "destination", out var dest) && !string.IsNullOrEmpty(dest))
            sb.AppendLine($"- **目标地址**: `{dest}`");

        if (TryGet(elem, "order", out var order))
            sb.AppendLine($"- **优先级**: `{order}`");

        return sb.ToString();
    }

    private static string FormatRemoveRouteDetails(JsonElement elem)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#### 已删除路由");

        if (TryGet(elem, "clusterId", out var clusterId))
            sb.AppendLine($"- **原属集群**: `{clusterId}`");

        if (TryGet(elem, "matchPath", out var matchPath))
            sb.AppendLine($"- **原匹配路径**: `{matchPath}`");

        return sb.ToString();
    }

    private static string FormatClusterDetails(JsonElement elem, bool isNew)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"#### {(isNew ? "新增集群配置" : "更新集群配置")}");

        if (TryGet(elem, "loadBalancingPolicy", out var lbp) && !string.IsNullOrEmpty(lbp))
            sb.AppendLine($"- **负载均衡**: `{lbp}`");

        if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty("destinations", out var dests))
        {
            if (dests.ValueKind == JsonValueKind.Object)
            {
                sb.AppendLine("- **目标节点**:");
                foreach (var dest in dests.EnumerateObject())
                {
                    var address = dest.Value.ValueKind == JsonValueKind.String
                        ? dest.Value.GetString() ?? ""
                        : (dest.Value.ValueKind == JsonValueKind.Object && TryGet(dest.Value, "address", out var addr) ? addr : dest.Value.ToString());
                    sb.AppendLine($"  - `{dest.Name}` → `{address}`");
                }
            }
            else if (dests.ValueKind == JsonValueKind.Number)
            {
                sb.AppendLine($"- **目标节点数**: {dests.GetInt32()}");
            }
            else if (dests.ValueKind == JsonValueKind.String)
            {
                sb.AppendLine($"- **目标节点**: `{dests.GetString()}`");
            }
        }

        return sb.ToString();
    }

    private static string FormatRemoveClusterDetails(JsonElement elem)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#### 已删除集群");

        if (elem.TryGetProperty("destinations", out var dests) && dests.ValueKind == JsonValueKind.Number)
            sb.AppendLine($"- **原目标节点数**: {dests.GetInt32()}");

        return sb.ToString();
    }

    private static string FormatRenameClusterDetails(JsonElement elem)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#### 集群重命名详情");

        if (TryGet(elem, "oldClusterId", out var oldId))
            sb.AppendLine($"- **原名称**: `{oldId}`");

        if (TryGet(elem, "newClusterId", out var newId))
            sb.AppendLine($"- **新名称**: `{newId}`");

        if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty("routesUpdated", out var routes) && routes.ValueKind == JsonValueKind.Number)
            sb.AppendLine($"- **受影响路由数**: {routes.GetInt32()}");

        if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty("destinations", out var dests) && dests.ValueKind == JsonValueKind.Object)
        {
            sb.AppendLine("- **当前目标节点**:");
            foreach (var dest in dests.EnumerateObject())
            {
                var address = dest.Value.ValueKind == JsonValueKind.String
                    ? dest.Value.GetString() ?? ""
                    : TryGet(dest.Value, "address", out var addr) ? addr : dest.Value.ToString();
                sb.AppendLine($"  - `{dest.Name}` → `{address}`");
            }
        }

        return sb.ToString();
    }

    private static string FormatRollbackDetails(JsonElement elem)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#### 配置回滚详情");

        if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty("routeCount", out var routes) && routes.ValueKind == JsonValueKind.Number)
            sb.AppendLine($"- **路由数**: {routes.GetInt32()}");

        if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty("clusterCount", out var clusters) && clusters.ValueKind == JsonValueKind.Number)
            sb.AppendLine($"- **集群数**: {clusters.GetInt32()}");

        return sb.ToString();
    }

    private static string FormatTestDetails(JsonElement elem)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#### 测试消息");
        if (TryGet(elem, "message", out var msg))
            sb.AppendLine($"- {msg}");
        else
            sb.AppendLine("- 这是一条来自 YARP Dashboard 的测试推送");
        return sb.ToString();
    }

    private static string FormatGenericDetails(JsonElement elem)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#### 详细信息");

        if (elem.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in elem.EnumerateObject())
            {
                var value = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? "",
                    JsonValueKind.Number => prop.Value.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => prop.Value.ToString()
                };
                sb.AppendLine($"- **{prop.Name}**: `{value}`");
            }
        }
        else
        {
            sb.AppendLine($"```json\n{elem}\n```");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Try to get a string value from a JsonElement by property name.
    /// </summary>
    private static bool TryGet(JsonElement elem, string propertyName, out string value)
    {
        value = string.Empty;
        if (elem.ValueKind != JsonValueKind.Object)
            return false;
        if (!elem.TryGetProperty(propertyName, out var prop))
            return false;

        value = prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString() ?? "",
            JsonValueKind.Number => prop.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => prop.ToString()
        };
        return true;
    }

    /// <summary>
    /// Compute DingTalk signature: Base64(HmacSHA256(secret, stringToSign)) → UrlEncode.
    /// </summary>
    private static string ComputeDingTalkSign(string stringToSign, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var signData = Encoding.UTF8.GetBytes(stringToSign);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(signData);
        return Uri.EscapeDataString(Convert.ToBase64String(hash));
    }
}
