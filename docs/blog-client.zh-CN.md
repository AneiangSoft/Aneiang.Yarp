# [系列 01] 微服务接入网关只需一行代码？Aneiang.Yarp.Client 深度解析

> **Aneiang.Yarp 源码解析系列** — [上一篇：00 - 项目总览](./blog-introduction.zh-CN.md) | [目录](./series-index.zh-CN.md) | [下一篇：02 - 网关核心模块](./blog-core.zh-CN.md)
>
> 在微服务架构中，每个服务接入 API 网关通常需要手动配置路由和集群地址。而 Aneiang.Yarp.Client 让这个过程变成了一行代码的事——服务启动自动注册，关闭自动注销，智能推断所有默认值。

---

## 背景：微服务注册的痛点

用 YARP 做网关时，每新增一个微服务，你需要：

1. 在网关的 `appsettings.json` 里加一条 Route 配置
2. 在网关的 `appsettings.json` 里加一条 Cluster 配置，填上服务地址
3. 重启网关让配置生效
4. 服务下线时记得删配置，不然请求会打到已停止的实例

如果团队有 20 个微服务，频繁的发布上线让这个过程变得非常痛苦。

**Aneiang.Yarp.Client 解决的就是这个问题。**

---

## 零依赖设计：客户端不碰 YARP

这是 Aneiang.Yarp.Client 最巧妙的设计决策之一：

```
Aneiang.Yarp.Client 的 NuGet 依赖：
  └── Microsoft.AspNetCore.App（框架引用）

没有！YARP！依！赖！
```

看它的 `.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <!-- 没有 PackageReference，只依赖框架 -->
</Project>
```

这意味着下游微服务引用 `Aneiang.Yarp.Client` 时，**不会间接拉入 YARP SDK 的整个依赖树**。你的微服务保持干净，只做业务逻辑。

三个包统一使用 `Aneiang.Yarp.Extensions` 命名空间，现有代码迁移零成本。

---

## 一行代码接入

### 最简示例

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAneiangYarpClient();  // 就这一行

var app = builder.Build();
app.MapControllers();
app.Run();
```

```json
// appsettings.json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://192.168.1.100:5000"
    }
  }
}
```

配一个网关地址，完事。`RouteName`、`ClusterName`、`MatchPath`、`DestinationAddress` 全部自动推断。

### 代码覆盖配置

也可以通过代码传参，优先级高于配置文件：

```csharp
builder.Services.AddAneiangYarpClient(options =>
{
    options.GatewayUrl = "http://192.168.1.100:5000";
    options.MatchPath = "/api/my-service/{**catch-all}";
    options.Order = 50;
});
```

---

## 智能默认值：`RegistrationOptionsResolver`

只配 `GatewayUrl` 就够用的秘密在于 `RegistrationOptionsResolver`——它会在注册前检查每个配置项，缺失的用默认值补齐：

| 配置项 | 默认值逻辑 | 说明 |
|--------|-----------|------|
| `RouteName` | `Assembly.GetEntryAssembly()?.GetName().Name` | 入口程序集名称，如 `SampleLocalService` |
| `ClusterName` | 同 `RouteName` | 路由和集群同名，简化管理 |
| `MatchPath` | `/{**catch-all}` | 匹配所有路径，兜底方案 |
| `DestinationAddress` | 从 Kestrel/Urls 环境变量自动检测 | 支持 localhost 替换为局域网 IP |
| `Order` | `50` | 路由优先级 |
| `Enabled` | 配置了 `GatewayUrl` 即为 true | 不配网关地址则不注册 |

**亮点**：`DestinationAddress` 不是简单地读取配置值，而是通过 `KestrelAutoConfigService` 从 `IServer` 的 Features 获取 Kestrel 实际绑定的监听地址。

---

## localhost 自动解析为局域网 IP

这是个非常实用的细节。开发时你习惯写：

```json
{
  "Urls": "http://localhost:5001"
}
```

但网关如果在另一台机器上，`localhost` 指向的是网关自己，根本转发不过来。

Aneiang.Yarp.Client 的 `GatewayAutoRegistrationClient` 在注册时会自动将 `localhost`、`127.0.0.1`、`[::1]` 替换为本机的局域网 IP：

```
localhost:5001  →  192.168.1.20:5001
127.0.0.1:5001  →  192.168.1.20:5001
[::1]:5001      →  192.168.1.20:5001
```

你不需要改任何配置，注册到网关的地址自动就是可达的。

---

## Kestrel 自动配置：`UseYarpKestrelAutoConfig()`

还有一个辅助扩展方法：

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.UseYarpKestrelAutoConfig();  // 自动检测 Kestrel 配置
```

它的作用是在服务启动时检查 Kestrel 是否监听了 `0.0.0.0`。如果没有，会在日志中输出详细的配置建议：

```
[WARNING] Kestrel is not listening on 0.0.0.0. Gateway may not reach this service.
  Current: http://localhost:5001
  Suggested: Add "Urls": "http://0.0.0.0:5001" to appsettings.json
```

它**不会自动修改你的配置**（尊重开发者的意愿），但会清楚地告诉你需要改什么。支持从三种来源检测：

- `Kestrel:EndPoints` 配置
- `Urls` 属性
- `ASPNETCORE_URLS` 环境变量

---

## 生命周期管理：`GatewayRegistrationHostedService`

自动注册/注销的核心是一个 `IHostedService` 实现：

```
服务启动
  └── StartAsync()
       ├── 1. 调用 RegistrationOptionsResolver 解析配置
       ├── 2. GatewayAutoRegistrationClient.RegisterAsync()
       │     └── POST /api/gateway/register-route
       ├── 3. 成功 → 启动心跳定时器（每30秒）
       └── 4. 失败 → 指数退避重试（最多5次）

服务关闭
  └── StopAsync()
       ├── 1. 停止心跳定时器
       └── 2. GatewayAutoRegistrationClient.UnregisterAsync()
             └── DELETE /api/gateway/{routeName}
```

### 指数退避重试

网关可能还没启动，或者网络还没就绪。重试策略：

```
第1次：2秒后重试
第2次：4秒后重试
第3次：8秒后重试
第4次：16秒后重试
第5次：30秒后重试
超过5次：放弃，记录错误日志
```

总等待时间约 60 秒，对于容器编排场景（K8s Pod 启动顺序不确定）非常友好。

### 心跳保活

注册成功后，每 30 秒向网关发送一次心跳：

```http
POST /api/gateway/heartbeat?routeName=sample-local-service
```

网关收到心跳后更新该路由的 `LastHeartbeat` 时间戳。配合 Dashboard 可以直观地看到每个服务的在线状态。

### 优雅关闭

`StopAsync()` 由 ASP.NET Core 的优雅关闭机制触发，确保服务下线时网关立即感知。在 K8s 的 `terminationGracePeriodSeconds` 窗口内完成注销，避免请求路由到已停止的 Pod。

---

## 多种认证方式

客户端连接网关时可能需要认证。`GatewayRegistrationOptions` 支持：

| 认证方式 | 配置项 | 说明 |
|---------|--------|------|
| BasicAuth | `AuthUsername` + `AuthPassword` | HTTP Basic 认证 |
| ApiKey | `ApiKey` + `ApiKeyHeaderName` | 通过 Header 传递 |
| Bearer | `AuthToken` | JWT Bearer Token |

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://localhost:5000",
      "AuthUsername": "admin",
      "AuthPassword": "demo123"
    }
  }
}
```

**更妙的是**：如果网关端调用了 `AddGatewayApiAuth()`，系统会自动从 Dashboard 的 JWT 密码推断凭据。客户端**完全不需要配认证信息**，一行代码就够：

```csharp
// 网关端
builder.Services.AddGatewayApiAuth();  // 自动读取 Dashboard JWT 密码

// 客户端 — 什么都不用配
builder.Services.AddAneiangYarpClient();
```

---

## IP 隔离模式

这是专为**团队多人开发调试**设计的特性。启用后：

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://localhost:5000",
      "UseIpIsolation": true
    }
  }
}
```

每个开发者的本地服务注册到**同一个路由路径**，网关根据请求来源 IP 自动路由到对应实例：

```
开发者 A (192.168.1.10) → POST /api/user → 网关 → 192.168.1.10:5001
开发者 B (192.168.1.20) → POST /api/user → 网关 → 192.168.1.20:5001
其他请求                  → POST /api/user → 网关 → 第一个可用实例
```

前端完全无感知，不需要改任何代码。详见 [IP 隔离负载均衡原理与实践](./blog-ip-isolation.zh-CN.md)。

---

## 完整示例：SampleLocalService

项目中的 `samples/SampleLocalService` 展示了最完整的用法：

```csharp
// samples/SampleLocalService/Program.cs
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.UseYarpKestrelAutoConfig();
builder.Services.AddAneiangYarpClient();
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

服务提供三个 API：

```csharp
[ApiController]
[Route("/api/local-service/[action]")]
public class LocalServiceController : ControllerBase
{
    [HttpGet] public IActionResult Ping() { ... }
    [HttpGet] public IActionResult Echo(string? message) { ... }
    [HttpPost] public IActionResult EchoPost(object? body) { ... }
}
```

启动后自动注册到网关，通过 `http://网关地址:5000/api/local-service/ping` 即可访问。

---

## 设计亮点总结

| 特性 | 说明 |
|------|------|
| **零 YARP 依赖** | 客户端包不依赖 YARP SDK，下游服务保持干净 |
| **一行代码接入** | `AddAneiangYarpClient()` 搞定一切 |
| **智能默认值** | 只需配 `GatewayUrl`，RouteName/MatchPath/Address 全自动 |
| **localhost 解析** | 自动替换为局域网 IP，跨机器可达 |
| **指数退避重试** | 2s→4s→8s→16s→30s，容器场景友好 |
| **心跳保活** | 30 秒心跳，配合 Dashboard 在线状态监控 |
| **优雅关闭** | IHostedService 确保服务下线前完成注销 |
| **Kestrel 检测** | 自动检测监听配置，给出修改建议 |
| **认证灵活** | BasicAuth/ApiKey/Bearer，支持智能推断 |

---

## 项目信息

- **GitHub**: [https://github.com/aneiang/Aneiang.Yarp](https://github.com/aneiang/Aneiang.Yarp)
- **NuGet**: `dotnet add package Aneiang.Yarp.Client`
- **协议**: MIT

```bash
# 接入只需两步
dotnet add package Aneiang.Yarp.Client
# Program.cs 加一行: builder.Services.AddAneiangYarpClient();
```

*（全文完）*

---

> **Aneiang.Yarp 源码解析系列** — [上一篇：00 - 项目总览](./blog-introduction.zh-CN.md) | [目录](./series-index.zh-CN.md) | [下一篇：02 - 网关核心模块](./blog-core.zh-CN.md)
