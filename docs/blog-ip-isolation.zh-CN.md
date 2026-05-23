# 多人开发不再冲突：YARP 网关的 IP 隔离负载均衡方案

> **Aneiang.Yarp 源码解析系列（篇 04）**
> | [上一篇：可视化 Dashboard](./blog-dashboard.zh-CN.md) | [系列目录](./series-index.zh-CN.md) |

多个开发者同时开发同一个微服务，各自在本地启动实例。传统方案需要每个开发者用不同的路径前缀，前端频繁切换，非常痛苦。

Aneiang.Yarp 基于 YARP 原生的 `ILoadBalancingPolicy`，实现了一套 **IP 隔离负载均衡** —— 所有开发者共用同一个路由路径，网关按请求来源 IP 自动路由到对应实例，前端完全无感。

**本文你会了解到：**

- 传统路径前缀隔离方案的痛点
- IP 隔离方案的整体架构和路由流程
- `ILoadBalancingPolicy` 自定义实现的三个关键组件
- 零分配 IP 解析的 `string.Create` + `ReadOnlySpan` 技巧
- 优雅的注销机制：不影响其他开发者的实例

---

## 问题：传统方案有多痛苦

团队中 3 个人同时开发用户服务，各自在本地跑实例：

```
开发者 A：http://192.168.1.10:5001
开发者 B：http://192.168.1.20:5001
开发者 C：http://192.168.1.30:5001
```

**传统做法** —— 每个人配一个路径前缀：

```
/api/user-dev-a/{**catchAll} → 192.168.1.10:5001
/api/user-dev-b/{**catchAll} → 192.168.1.20:5001
/api/user-dev-c/{**catchAll} → 192.168.1.30:5001
```

**痛点**：

- 前端要改 API 基础路径，切换成本高
- 经常忘记切换，调试到别人的服务上去
- 新人加入要手动加路由规则
- 路由规则越积越多

## 方案：同一个 URL，自动路由到你的实例

IP 隔离的效果：

```
开发者 A 的浏览器 → POST /api/user → 网关 → 192.168.1.10:5001
开发者 B 的浏览器 → POST /api/user → 网关 → 192.168.1.20:5001
其他人访问         → POST /api/user → 网关 → 第一个可用实例
```

前端代码不用改，`/api/user` 就是 `/api/user`。

---

## 整体架构

```
┌──────────┐    ┌───────────────────────────────┐    ┌──────────┐
│ 开发者 A  │───→│     网关 (Aneiang.Yarp)       │───→│ A 的实例  │
│ IP:.1.10 │    │  Route: /api/user              │    │ :5001    │
└──────────┘    │  Cluster: user-service          │    └──────────┘
                │  LoadBalancingPolicy: IpBased   │
┌──────────┐    │                               │    ┌──────────┐
│ 开发者 B  │───→│  匹配逻辑：                    │───→│ B 的实例  │
│ IP:.1.20 │    │  1. 获取请求来源 IP             │    │ :5001    │
└──────────┘    │  2. 匹配 Destination Metadata   │    └──────────┘
                │  3. 无匹配 → 第一个可用实例      │
```

三个关键组件协同工作：

1. **客户端注册时携带 IP** — `GatewayAutoRegistrationClient`
2. **网关创建 IP 绑定的 Destination** — `DynamicYarpConfigService`
3. **请求时按 IP 匹配 Destination** — `IpBasedLoadBalancingPolicy`

---

## 组件一：客户端携带 IP 注册

```csharp
public async Task<bool> RegisterAsync(GatewayRegistrationOptions options)
{
    var request = new RegisterRouteRequest
    {
        RouteName = options.RouteName,
        MatchPath = options.MatchPath,
        DestinationAddress = resolvedAddress,
        UseIpIsolation = options.UseIpIsolation,
        ClientIp = options.UseIpIsolation ? GetLocalIpAddress() : null  // 自动获取本机 IP
    };
    await _httpClient.PostAsJsonAsync("/api/gateway/register-route", request);
}
```

当 `UseIpIsolation: true` 时，注册请求自动携带客户端局域网 IP。

---

## 组件二：网关创建 IP 绑定的 Destination

```csharp
// DynamicYarpConfigService.TryAddRoute 中
if (request.UseIpIsolation && !string.IsNullOrEmpty(request.ClientIp))
{
    var destKey = $"ip-{request.ClientIp.Replace(".", "-")}";  // 如 "ip-192-168-1-10"
    var destination = new DestinationConfig
    {
        Address = request.DestinationAddress,
        Metadata = new Dictionary<string, string>
        {
            ["ClientIp"] = request.ClientIp  // 标记：这个 Destination 属于哪个 IP
        }
    };
    cluster.Destinations[destKey] = destination;
    cluster.LoadBalancingPolicy = "IpBased";  // 切换到 IP 负载均衡策略
}
```

注册后的集群配置（持久化到 `gateway-dynamic.json`）：

```json
{
  "sample-local-service": {
    "Destinations": {
      "ip-192-168-1-10": {
        "Address": "http://192.168.1.10:5001",
        "Metadata": { "ClientIp": "192.168.1.10" }
      },
      "ip-192-168-1-20": {
        "Address": "http://192.168.1.20:5001",
        "Metadata": { "ClientIp": "192.168.1.20" }
      }
    },
    "LoadBalancingPolicy": "IpBased"
  }
}
```

> **为什么同时用 Key 和 Metadata？** Metadata 是 YARP 标准机制，正常情况下用于匹配。Key 格式约定提供了额外的恢复能力 —— 即使 Metadata 丢失，也能从 Key 中解析出 IP。

---

## 组件三：IP 匹配负载均衡

核心实现 `IpBasedLoadBalancingPolicy`：

```csharp
internal sealed class IpBasedLoadBalancingPolicy : ILoadBalancingPolicy
{
    public string Name => "IpBased";

    public DestinationState? PickDestination(
        IReadOnlyList<DestinationState> availableDestinations,
        HttpContext context)
    {
        var clientIp = GetClientIpAddress(context);
        if (string.IsNullOrEmpty(clientIp))
            return availableDestinations.FirstOrDefault();

        // 第一轮：匹配 Metadata["ClientIp"]
        foreach (var dest in availableDestinations)
            if (dest.Metadata?.TryGetValue("ClientIp", out var ip) == true
                && ip.Equals(clientIp, StringComparison.OrdinalIgnoreCase))
                return dest;

        // 第二轮：从 Destination ID 解析 IP（持久化恢复场景）
        foreach (var dest in availableDestinations)
            if (RestoreIpAddress(dest.DestinationId) is string restored
                && restored.Equals(clientIp, StringComparison.OrdinalIgnoreCase))
                return dest;

        // 兜底：返回第一个可用实例
        return availableDestinations.FirstOrDefault();
    }
}
```

### 零分配 IP 解析

从 `"ip-192-168-1-10"` 还原为 `"192.168.1.10"`，使用 `string.Create` + `ReadOnlySpan` 实现零堆分配：

```csharp
private static string? RestoreIpAddress(string destinationId)
{
    const string prefix = "ip-";
    if (!destinationId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        return null;

    return string.Create(destinationId.Length - prefix.Length, destinationId, (span, id) =>
    {
        var source = id.AsSpan(prefix.Length);
        for (int i = 0; i < source.Length; i++)
            span[i] = source[i] == '-' ? '.' : source[i];
    });
}
```

没有 `String.Replace()` 的中间字符串，纯栈上操作，高并发下零 GC 压力。

### 高效的客户端 IP 获取

```csharp
private static string? GetClientIpAddress(HttpContext context)
{
    // 优先读取 X-Forwarded-For（Nginx/Caddy 等代理场景）
    var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrEmpty(forwardedFor))
    {
        var span = forwardedFor.AsSpan();
        var commaIndex = span.LastIndexOf(',');
        var ipSpan = commaIndex >= 0 ? span.Slice(commaIndex + 1).Trim() : span.Trim();
        return StripPort(ipSpan);
    }
    return context.Connection.RemoteIpAddress?.ToString();
}
```

全程 `ReadOnlySpan<char>` 操作，手动解析避免 `string.Split()` 的分配。还处理了 IPv4 端口后缀和 IPv6 方括号格式。

---

## 优雅的注销

开发者关闭服务时，只移除自己 IP 对应的 Destination，不影响其他人：

```http
DELETE /api/gateway/sample-local-service?clientIp=192.168.1.10
```

处理逻辑：

```
1. 找到路由 → 找到
2. 有 clientIp 参数 → 从集群中只移除 ip-192-168-1-10
3. 集群中还有其他 Destination？ → 保留路由（其他人还在用）
4. 集群中没有任何 Destination？ → 删除集群和路由（最后一个下线）
```

不会出现"一个人关了服务，其他人的路由也丢了"的情况。

---

## 使用方式

### 客户端：一行配置

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://网关地址:5000",
      "UseIpIsolation": true
    }
  }
}
```

就这一行 `UseIpIsolation: true`，其他全自动。

### 网关端：无需配置

`IpBasedLoadBalancingPolicy` 在 `AddAneiangYarp()` 中自动注册。客户端启用 IP 隔离后，集群自动切换到 `IpBased` 策略。

### Nginx 代理场景

开发环境有 Nginx 时，确保传递客户端真实 IP：

```nginx
location / {
    proxy_pass http://gateway:5000;
    proxy_set_header X-Forwarded-For $remote_addr;
}
```

`GetClientIpAddress()` 优先读取 `X-Forwarded-For`，代理场景正常工作。

---

## 完整示例

```bash
# 终端 1 — 启动网关
dotnet run --project samples/SampleGateway

# 终端 2 — 开发者 A 启动本地服务
dotnet run --project samples/SampleLocalService
# → 自动注册: ip-192-168-1-10:5001

# 终端 3 — 开发者 B（另一台机器）
dotnet run --project samples/SampleLocalService
# → 自动注册: ip-192-168-1-20:5001

# 两人访问同一个 URL，自动路由到各自的实例
curl http://网关:5000/api/local-service/ping
```

---

## 设计亮点

| 特性 | 实现 | 优势 |
|------|------|------|
| 无感路由 | 自定义 ILoadBalancingPolicy | 前端完全不改代码 |
| 零分配解析 | string.Create + ReadOnlySpan | 高并发零 GC 压力 |
| 双源 IP 匹配 | Metadata + DestinationId | 持久化重启后仍可用 |
| 精确注销 | 按 clientIp 移除单个 Destination | 不影响其他开发者 |
| 自动兜底 | 无匹配返回第一个可用实例 | 未注册 IP 也能用 |
| 代理兼容 | X-Forwarded-For 优先 | Nginx/Caddy 后正常工作 |

**源码地址**：[https://github.com/aneiang/Aneiang.Yarp](https://github.com/aneiang/Aneiang.Yarp)

```bash
# 客户端
dotnet add package Aneiang.Yarp.Client
# appsettings.json 加一行: "UseIpIsolation": true

# 网关端无需额外配置
```

---

> **Aneiang.Yarp 源码解析系列**
>
> | [上一篇：可视化 Dashboard](./blog-dashboard.zh-CN.md) | [系列目录](./series-index.zh-CN.md) |
>
> 觉得有用？去 [GitHub 点个 Star](https://github.com/aneiang/Aneiang.Yarp) 支持一下。
