# 微服务接入网关只需一行代码？Aneiang.Yarp.Client 源码解析

> **Aneiang.Yarp 源码解析系列（篇 01）**
> | [上一篇：项目总览](./blog-introduction.zh-CN.md) | [下一篇：网关核心模块架构解析](./blog-core.zh-CN.md) | [系列目录](./series-index.zh-CN.md) |

在微服务架构中，每个服务接入 API 网关通常需要手动在网关配置路由和集群地址。今天拆解的 **Aneiang.Yarp.Client** 让这个过程变成了一行代码的事 —— 服务启动自动注册，关闭自动注销，所有默认值自动推断。

**本文你会了解到：**

- 客户端包如何做到零 YARP 依赖
- 智能默认值是怎么推断出来的
- localhost 为什么能自动解析为局域网 IP
- 注册失败时的指数退避重试策略
- 心跳保活和优雅关闭的实现细节

---

## 先看效果

```csharp
// 客户端 Program.cs — 就这一行
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAneiangYarpClient();

var app = builder.Build();
app.MapControllers();
app.Run();
```

```json
// appsettings.json — 只需配网关地址
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://192.168.1.100:5000"
    }
  }
}
```

配一个网关地址，完事。`RouteName`、`ClusterName`、`MatchPath`、`DestinationAddress` 全部自动推断。

---

## 零 YARP 依赖设计

这是 Aneiang.Yarp.Client 最巧妙的设计决策。

看它的 `.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <!-- 没有 PackageReference，只依赖框架 -->
</Project>
```

**没有 YARP 依赖。**

它的 NuGet 依赖树：

```
Aneiang.Yarp.Client
  └── Microsoft.AspNetCore.App（框架引用）
```

这意味着下游微服务引用 `Aneiang.Yarp.Client` 时，不会间接拉入 YARP SDK 的整个依赖树。你的微服务保持干净，只做业务逻辑。

三个包统一使用 `Aneiang.Yarp.Extensions` 命名空间，现有代码迁移零成本。

> **设计思考**：如果客户端包依赖了 YARP SDK，那么每个微服务都会间接引入反向代理的完整依赖链（包括 HTTP 转换、负载均衡、健康检查等），这对下游服务来说完全没有必要。零依赖设计让职责边界清晰。

---

## 智能默认值：只配 GatewayUrl 就够用

只配一个网关地址就能跑，背后的秘密在 `RegistrationOptionsResolver`。它会在注册前逐项检查，缺失的用默认值补齐：

| 配置项 | 默认值 | 推断逻辑 |
|--------|--------|---------|
| `RouteName` | `SampleLocalService` | `Assembly.GetEntryAssembly()?.GetName().Name` |
| `ClusterName` | 同 RouteName | 路由和集群同名，简化管理 |
| `MatchPath` | `/{**catch-all}` | 匹配所有路径的兜底方案 |
| `DestinationAddress` | `http://192.168.1.20:5001` | 从 Kestrel 绑定地址自动检测 |
| `Order` | `50` | YARP 路由优先级（值越小优先级越高） |
| `Enabled` | `true` | 配置了 GatewayUrl 即自动启用 |

其中 `DestinationAddress` 的检测比较有意思 —— 它不是简单地读配置值，而是通过 `KestrelAutoConfigService` 从 ASP.NET Core 的 `IServer` Features 获取 Kestrel 实际监听的地址，支持三种来源：

- `Kestrel:EndPoints` 配置节
- `Urls` 属性
- `ASPNETCORE_URLS` 环境变量

当然，你也可以通过代码覆盖默认值（优先级高于配置文件）：

```csharp
builder.Services.AddAneiangYarpClient(options =>
{
    options.GatewayUrl = "http://192.168.1.100:5000";
    options.MatchPath = "/api/my-service/{**catch-all}";
    options.Order = 50;
});
```

---

## localhost 自动解析为局域网 IP

这是个非常实用的细节。

开发时你习惯写：

```json
{
  "Urls": "http://localhost:5001"
}
```

但网关如果在另一台机器上，`localhost` 指向的是网关自己，请求根本转发不过来。

`GatewayAutoRegistrationClient` 在注册时自动将 `localhost`、`127.0.0.1`、`[::1]` 替换为本机局域网 IP：

```
localhost:5001  →  192.168.1.20:5001
127.0.0.1:5001  →  192.168.1.20:5001
[::1]:5001      →  192.168.1.20:5001
```

你不需要改任何配置，注册到网关的地址自动就是可达的。

---

## Kestrel 自动配置检测

`UseYarpKestrelAutoConfig()` 是一个辅助扩展方法：

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.UseYarpKestrelAutoConfig();
```

它会在服务启动时检查 Kestrel 是否监听了 `0.0.0.0`。如果没有，日志中输出配置建议：

```
[WARNING] Kestrel is not listening on 0.0.0.0. Gateway may not reach this service.
  Current: http://localhost:5001
  Suggested: Add "Urls": "http://0.0.0.0:5001" to appsettings.json
```

**它不会自动修改你的配置**（尊重开发者的意愿），但会清楚地告诉你需要改什么。

---

## 生命周期管理

自动注册和注销的核心是 `GatewayRegistrationHostedService`，一个 `IHostedService` 实现：

```
服务启动 → StartAsync()
  ├── 1. RegistrationOptionsResolver 解析配置
  ├── 2. GatewayAutoRegistrationClient.RegisterAsync()
  │     └── POST /api/gateway/register-route
  ├── 3. 成功 → 启动心跳定时器（每 30 秒）
  └── 4. 失败 → 指数退避重试（最多 5 次）

服务关闭 → StopAsync()
  ├── 1. 停止心跳定时器
  └── 2. GatewayAutoRegistrationClient.UnregisterAsync()
        └── DELETE /api/gateway/{routeName}
```

### 指数退避重试

网关可能还没启动，或者网络还没就绪。重试间隔：

```
第 1 次：2 秒后重试
第 2 次：4 秒后重试
第 3 次：8 秒后重试
第 4 次：16 秒后重试
第 5 次：30 秒后重试
超过 5 次：放弃，记录错误日志
```

总等待时间约 60 秒。对于容器编排场景（K8s Pod 启动顺序不确定）非常友好。

### 心跳保活

注册成功后，每 30 秒向网关发送心跳：

```http
POST /api/gateway/heartbeat?routeName=sample-local-service
```

网关更新 `LastHeartbeat` 时间戳，配合 Dashboard 可以直观看到每个服务的在线状态。

### 优雅关闭

`StopAsync()` 由 ASP.NET Core 的优雅关闭机制触发，确保服务下线时网关立即感知。在 K8s 的 `terminationGracePeriodSeconds` 窗口内完成注销，避免请求路由到已停止的 Pod。

---

## 认证：配了 Dashboard 密码就够了

客户端支持三种认证方式连接网关：

| 方式 | 配置项 | 说明 |
|------|--------|------|
| BasicAuth | `AuthUsername` + `AuthPassword` | HTTP Basic |
| ApiKey | `ApiKey` + `ApiKeyHeaderName` | Header 传递 |
| Bearer | `AuthToken` | JWT Bearer Token |

但**最省心的方式**是什么都不配：

```csharp
// 网关端
builder.Services.AddGatewayApiAuth();  // 自动读取 Dashboard JWT 密码

// 客户端 — 什么都不用配
builder.Services.AddAneiangYarpClient();
```

`AddGatewayApiAuth()` 会自动从 Dashboard 的 `JwtPassword` 推断出 API 认证凭据（用户名 `admin`，密码 = JWT 密码），客户端零配置。

---

## IP 隔离：一行配置搞定

专为团队多人调试设计的特性。启用后，每个开发者的本地服务注册到同一个路由路径，网关按请求来源 IP 自动路由：

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

```
开发者 A (192.168.1.10) → POST /api/user → 网关 → A 的实例 :5001
开发者 B (192.168.1.20) → POST /api/user → 网关 → B 的实例 :5001
其他请求                  → POST /api/user → 网关 → 第一个可用实例
```

前端代码完全不变。原理和实现细节见本系列[篇 04](./blog-ip-isolation.zh-CN.md)。

---

## 完整示例

项目中的 `samples/SampleLocalService` 展示了完整用法：

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.UseYarpKestrelAutoConfig();       // 检测 Kestrel 配置
builder.Services.AddAneiangYarpClient();  // 自动注册到网关
builder.Services.AddControllers();

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.Run();
```

```json
// appsettings.json
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

启动后自动注册到网关，通过 `http://网关:5000/api/local-service/ping` 即可访问。

---

## 设计亮点

| 特性 | 实现 | 为什么重要 |
|------|------|-----------|
| 零 YARP 依赖 | 不引用 YARP SDK | 下游服务保持干净 |
| 一行代码接入 | `AddAneiangYarpClient()` | 接入成本极低 |
| 智能默认值 | 只需配 GatewayUrl | 减少配置出错概率 |
| localhost 解析 | 自动替换为局域网 IP | 跨机器可达，不踩坑 |
| 指数退避 | 2s → 4s → 8s → 16s → 30s | 容器场景友好 |
| 心跳保活 | 30 秒一次 | Dashboard 在线状态可见 |
| 优雅关闭 | IHostedService | K8s 下线不丢请求 |
| 认证智能推断 | 读取 Dashboard JWT 密码 | 客户端零配置 |

**源码地址**：[GitHub](https://github.com/AneiangSoft/Aneiang.Yarp) | [Gitee](https://gitee.com/aneiangsoft/aneiang-yarp)

```bash
dotnet add package Aneiang.Yarp.Client
# Program.cs 加一行: builder.Services.AddAneiangYarpClient();
```

---

> **Aneiang.Yarp 源码解析系列**
>
> | [上一篇：项目总览](./blog-introduction.zh-CN.md) | [下一篇：网关核心模块架构解析](./blog-core.zh-CN.md) | [系列目录](./series-index.zh-CN.md) |
>
> 觉得有用？去 [GitHub](https://github.com/AneiangSoft/Aneiang.Yarp) 或 [Gitee](https://gitee.com/aneiangsoft/aneiang-yarp) 点个 Star 支持一下。
