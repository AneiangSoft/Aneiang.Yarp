# [系列 04] 多人开发不再冲突：Aneiang.Yarp IP 隔离负载均衡原理与实践

> **Aneiang.Yarp 源码解析系列** — [上一篇：03 - 可视化 Dashboard](./blog-dashboard.zh-CN.md) | [目录](./series-index.zh-CN.md)
>
> 多个开发者同时开发同一个微服务，各自在本地启动实例。传统方案需要每个开发者用不同的路径前缀，前端频繁切换。Aneiang.Yarp 的 IP 隔离负载均衡让所有开发者共用同一个路由路径，网关按请求来源 IP 自动路由——前端完全无感。

---

## 一、问题场景

### 传统方案：路径前缀隔离

```
开发者 A 的本地服务：http://192.168.1.10:5001
开发者 B 的本地服务：http://192.168.1.20:5001

网关配置：
  /api/user-dev-a/{**catchAll} → 192.168.1.10:5001
  /api/user-dev-b/{**catchAll} → 192.168.1.20:5001
```

**痛点**：
- 每个开发者需要修改前端的 API 基础路径
- 路径前缀容易忘记切换，调试到别人的服务上去
- 新人加入需要手动加路由规则
- 路由规则越来越多，管理混乱

### IP 隔离方案：无感路由

```
开发者 A 的本地服务：http://192.168.1.10:5001
开发者 B 的本地服务：http://192.168.1.20:5001

网关配置：
  /api/user/{**catchAll} → 集群 "user-service"
    ├── destination "ip-192-168-1-10" → 192.168.1.10:5001（Metadata: ClientIp=192.168.1.10）
    └── destination "ip-192-168-1-20" → 192.168.1.20:5001（Metadata: ClientIp=192.168.1.20）
```

**效果**：

```
开发者 A 的浏览器 → POST /api/user → 网关 → 192.168.1.10:5001  ✅
开发者 B 的浏览器 → POST /api/user → 网关 → 192.168.1.20:5001  ✅
其他人访问         → POST /api/user → 网关 → 第一个可用实例     ✅
```

前端代码完全不变，`/api/user` 就是 `/api/user`。

---

## 二、实现原理

### 整体架构

```
┌──────────────┐     ┌──────────────────────────────┐     ┌─────────────────┐
│  开发者 A     │     │  网关 (Aneiang.Yarp)          │     │  开发者 A 本地   │
│  浏览器       │────→│  Route: /api/user             │────→│  服务 :5001     │
│  IP: .1.10   │     │  Cluster: user-service         │     │  IP: .1.10     │
└──────────────┘     │  LB Policy: IpBased           │     └─────────────────┘
                     │                                │
┌──────────────┐     │  Destination 匹配逻辑：        │     ┌─────────────────┐
│  开发者 B     │     │  1. 获取请求来源 IP             │     │  开发者 B 本地   │
│  浏览器       │────→│  2. 遍历 Destinations          │────→│  服务 :5001     │
│  IP: .1.20   │     │  3. 匹配 Metadata["ClientIp"]  │     │  IP: .1.20     │
└──────────────┘     │  4. 无匹配 → 回退第一个可用     │     └─────────────────┘
                     └──────────────────────────────┘
```

### 三个关键组件

#### 1. 客户端注册时携带 IP（`GatewayAutoRegistrationClient`）

```csharp
// 注册请求
public async Task<bool> RegisterAsync(GatewayRegistrationOptions options)
{
    var request = new RegisterRouteRequest
    {
        RouteName = options.RouteName,
        ClusterName = options.ClusterName,
        MatchPath = options.MatchPath,
        DestinationAddress = resolvedAddress,
        Order = options.Order,
        UseIpIsolation = options.UseIpIsolation,
        ClientIp = options.UseIpIsolation ? GetLocalIpAddress() : null
    };

    await _httpClient.PostAsJsonAsync("/api/gateway/register-route", request);
}
```

当 `UseIpIsolation: true` 时：
- 自动获取本机局域网 IP
- 在注册请求中携带 `ClientIp`
- 网关收到后创建 IP 绑定的 Destination

#### 2. 网关创建 IP 绑定的 Destination（`DynamicYarpConfigService`）

```csharp
// TryAddRoute 中的 IP 隔离逻辑
if (request.UseIpIsolation && !string.IsNullOrEmpty(request.ClientIp))
{
    var destKey = $"ip-{request.ClientIp.Replace(".", "-")}";
    var destination = new DestinationConfig
    {
        Address = request.DestinationAddress,
        Metadata = new Dictionary<string, string>
        {
            ["ClientIp"] = request.ClientIp  // 标记客户端 IP
        }
    };
    cluster.Destinations[destKey] = destination;
}
```

Destination Key 格式：`ip-192-168-1-10`

**为什么用 Key 而不只是 Metadata？**

因为配置持久化到 `gateway-dynamic.json` 后重启，内存中的 `IProxyConfigProvider` 会重新加载。Metadata 会被保留，但 Key 格式的约定提供了额外的恢复能力——即使 Metadata 丢失，也可以从 Key 中解析出 IP。

#### 3. IP 匹配负载均衡（`IpBasedLoadBalancingPolicy`）

```csharp
internal sealed class IpBasedLoadBalancingPolicy : ILoadBalancingPolicy
{
    public string Name => "IpBased";

    public DestinationState? PickDestination(
        IReadOnlyList<DestinationState> availableDestinations,
        HttpContext context)
    {
        var clientIp = GetClientIpAddress(context);
        if (string.IsNullOrEmpty(clientIp)) return availableDestinations.FirstOrDefault();

        // 优先匹配 Metadata["ClientIp"]
        foreach (var dest in availableDestinations)
        {
            if (dest.Metadata?.TryGetValue("ClientIp", out var ip) == true
                && ip.Equals(clientIp, StringComparison.OrdinalIgnoreCase))
            {
                return dest;
            }
        }

        // 兜底：从 Destination ID 解析 IP（持久化恢复场景）
        foreach (var dest in availableDestinations)
        {
            var restoredIp = RestoreIpAddress(dest.DestinationId);
            if (restoredIp != null && restoredIp.Equals(clientIp, StringComparison.OrdinalIgnoreCase))
            {
                return dest;
            }
        }

        // 无匹配 → 第一个可用实例（兜底）
        return availableDestinations.FirstOrDefault();
    }
}
```

**零分配 IP 解析**：

```csharp
// RestoreIpAddress — 从 "ip-192-168-1-10" 还原为 "192.168.1.10"
private static string? RestoreIpAddress(string destinationId)
{
    const string prefix = "ip-";
    if (!destinationId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        return null;

    // string.Create + ReadOnlySpan 零分配解析
    return string.Create(destinationId.Length - prefix.Length, destinationId, (span, id) =>
    {
        var source = id.AsSpan(prefix.Length);
        for (int i = 0; i < source.Length; i++)
        {
            span[i] = source[i] == '-' ? '.' : source[i];
        }
    });
}
```

没有 `String.Replace()` 的中间字符串分配，纯栈上操作。

**高效的 IP 获取**：

```csharp
private static string? GetClientIpAddress(HttpContext context)
{
    // 优先读取 X-Forwarded-For（代理场景）
    var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrEmpty(forwardedFor))
    {
        // 手动解析，避免 string.Split() 分配
        var span = forwardedFor.AsSpan();
        var commaIndex = span.LastIndexOf(',');
        var ipSpan = commaIndex >= 0 ? span.Slice(commaIndex + 1).Trim() : span.Trim();
        return StripPort(ipSpan);
    }

    // 回退到 RemoteIpAddress
    return context.Connection.RemoteIpAddress?.ToString();
}

// 处理 IPv6 端口后缀 "[::1]:5001"
private static string? StripPort(ReadOnlySpan<char> ipSpan)
{
    var lastColon = ipSpan.LastIndexOf(':');
    if (lastColon < 0) return ipSpan.ToString();

    // IPv4 端口格式 "192.168.1.10:5001" — 取冒号前面
    // IPv6 格式 "[::1]:5001" — 取方括号内
    if (!ipSpan.Slice(0, lastColon).Contains('['))
        return ipSpan.Slice(0, lastColon).ToString();

    var closeBracket = ipSpan.IndexOf(']');
    return closeBracket >= 0 ? ipSpan.Slice(1, closeBracket - 1).ToString() : ipSpan.ToString();
}
```

全程使用 `ReadOnlySpan<char>` 操作，**零堆分配**。在高并发场景下，这意味着更少的 GC 压力。

---

## 三、优雅的注销机制

当开发者关闭本地服务时，只需要移除自己 IP 对应的 Destination，不影响其他开发者的实例：

```http
DELETE /api/gateway/sample-local-service?clientIp=192.168.1.10
```

`DynamicYarpConfigService.TryRemoveRoute()` 的处理逻辑：

```
1. 查找路由 → 找到
2. 检查是否有 clientIp 参数 → 有
3. 从集群中移除 ip-192-168-1-10 这个 Destination
4. 检查集群中是否还有其他 Destination
   ├── 有 → 保留集群和路由（其他开发者还在用）
   └── 没有 → 删除集群和路由
```

这样设计保证了：

- 开发者 A 关闭服务，不影响开发者 B
- 最后一个开发者关闭时，自动清理整个路由
- 不会出现"删了路由导致别人的服务不可达"的情况

---

## 四、使用方式

### 客户端配置

```json
// appsettings.json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://网关地址:5000",
      "UseIpIsolation": true
    }
  }
}
```

就这一行 `UseIpIsolation: true`，其他全部自动：

- 自动获取本机 IP
- 注册时携带 IP 信息
- 注销时只移除自己的 Destination

### 网关端无需额外配置

`IpBasedLoadBalancingPolicy` 在 `AddAneiangYarp()` 中自动注册。当客户端传入 `UseIpIsolation: true` 时，系统自动：

1. 创建带 IP 标记的 Destination
2. 将集群的负载均衡策略设为 `IpBased`

```csharp
// DynamicYarpConfigService.TryAddRoute 中
if (request.UseIpIsolation)
{
    clusterConfig.LoadBalancingPolicy = "IpBased";
}
```

### 集群配置示例（持久化后的 gateway-dynamic.json）

```json
{
  "Clusters": {
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
}
```

---

## 五、完整示例：SampleLocalService

```csharp
// samples/SampleLocalService/Program.cs
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.UseYarpKestrelAutoConfig();       // 自动检测 Kestrel 监听配置
builder.Services.AddAneiangYarpClient();  // 自动注册 + IP 隔离
builder.Services.AddControllers();

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.Run();
```

```json
// samples/SampleLocalService/appsettings.json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://localhost:5000",
      "RouteName": "sample-local-service",
      "MatchPath": "/api/local-service/{**catch-all}",
      "UseIpIsolation": true
    }
  },
  "Kestrel": {
    "EndPoints": {
      "Http": { "Url": "http://localhost:5001" }
    }
  }
}
```

**操作流程**：

```bash
# 终端 1 — 启动网关
dotnet run --project samples/SampleGateway

# 终端 2 — 开发者 A 启动本地服务
dotnet run --project samples/SampleLocalService
# → 自动注册: ip-192-168-1-10:5001

# 终端 3 — 开发者 B 启动本地服务（另一台机器或改端口）
dotnet run --project samples/SampleLocalService --urls "http://0.0.0.0:5002"
# → 自动注册: ip-192-168-1-20:5002

# 开发者 A 的浏览器访问
curl http://localhost:5000/api/local-service/ping
# → 路由到 192.168.1.10:5001

# 开发者 B 的浏览器访问
curl http://localhost:5000/api/local-service/ping
# → 路由到 192.168.1.20:5002
```

两个开发者使用**完全相同的 URL**，但请求被自动路由到各自的实例。

---

## 六、Nginx 代理场景

开发环境通常有 Nginx 等反向代理，需要正确传递客户端 IP：

```nginx
# nginx.conf
location / {
    proxy_pass http://gateway:5000;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Real-IP $remote_addr;
}
```

Aneiang.Yarp 的 `GetClientIpAddress()` 优先读取 `X-Forwarded-For` 头，在有反向代理的场景下也能正确获取客户端真实 IP。

---

## 七、设计亮点

| 特性 | 实现 | 优势 |
|------|------|------|
| **无感路由** | 自定义 ILoadBalancingPolicy | 前端完全不改代码 |
| **零分配 IP 解析** | string.Create + ReadOnlySpan | 高并发零 GC 压力 |
| **双源 IP 匹配** | Metadata + DestinationId | 持久化重启后仍可用 |
| **精确注销** | 按 clientIp 移除单个 Destination | 不影响其他开发者 |
| **自动兜底** | 无匹配时返回第一个可用实例 | 未注册的 IP 也能正常使用 |
| **代理兼容** | X-Forwarded-For 优先读取 | Nginx/Caddy 等代理后正常工作 |
| **一行配置** | UseIpIsolation: true | 零代码修改 |

---

## 项目信息

- **GitHub**: [https://github.com/aneiang/Aneiang.Yarp](https://github.com/aneiang/Aneiang.Yarp)
- **NuGet**: `dotnet add package Aneiang.Yarp.Client`（客户端）/ `dotnet add package Aneiang.Yarp`（网关端）
- **协议**: MIT

```bash
# 客户端
dotnet add package Aneiang.Yarp.Client
# appsettings.json 加一行: "UseIpIsolation": true

# 网关端无需额外配置，AddAneiangYarp() 已包含 IpBased 负载均衡
```

*（全文完）*

---

> **Aneiang.Yarp 源码解析系列** — [上一篇：03 - 可视化 Dashboard](./blog-dashboard.zh-CN.md) | [目录](./series-index.zh-CN.md)
