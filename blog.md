# Aneiang.Yarp：基于 YARP 的 .NET API 网关增强方案

> 一个开箱即用的 YARP 网关增强库，提供可视化 Dashboard、动态路由管理、客户端自动注册和 IP 隔离负载均衡能力。

## 项目简介

[Aneiang.Yarp](https://github.com/aneiang/Aneiang.Yarp) 是基于微软官方 [YARP (Yet Another Reverse Proxy)](https://microsoft.github.io/reverse-proxy/) 2.3.0 构建的 API 网关增强方案，采用 MIT 协议开源，支持 .NET 8.0 和 .NET 9.0。

它解决了 YARP 在实际生产使用中的几个痛点：

- **YARP 本身只提供反向代理核心能力**，缺乏可视化管理界面
- **路由配置依赖文件重启**，缺少运行时动态管理能力
- **微服务接入网关需要手动配置**，缺少客户端自动注册机制
- **开发环境多实例调试困难**，每个开发者都要配不同的端口和路由

Aneiang.Yarp 通过三个 NuGet 包来解决这些问题。

---

## NuGet 包结构

| 包名 | 用途 | 依赖 |
|------|------|------|
| `Aneiang.Yarp` | 网关核心库 | YARP.ReverseProxy 2.3.0 |
| `Aneiang.Yarp.Client` | 客户端自动注册（轻量，仅 ASP.NET Core） | 无 YARP 依赖 |
| `Aneiang.Yarp.Dashboard` | 可视化管理面板 | Aneiang.Yarp |

```
依赖关系：

Aneiang.Yarp.Dashboard
  └── Aneiang.Yarp
        └── Aneiang.Yarp.Client

客户端服务 → 仅引用 Aneiang.Yarp.Client（不拉入 YARP SDK）
网关服务   → 引用 Aneiang.Yarp + Aneiang.Yarp.Dashboard
```

> **设计决策**：客户端包 `Aneiang.Yarp.Client` **不依赖 YARP SDK**，只依赖 `Microsoft.AspNetCore.App` 框架引用。这意味着下游微服务接入网关时，不会间接引入整个 YARP 包，保持引用干净。三个包共享 `Aneiang.Yarp.Extensions` 命名空间，现有代码迁移零成本。

---

## 核心功能

### 一、动态路由管理

YARP 原生的路由管理依赖于 `appsettings.json` 和 `IProxyConfigProvider`，修改配置后需要重启。Aneiang.Yarp 在此基础上实现了完整的运行时动态管理能力。

#### 核心机制

Aneiang.Yarp 使用 YARP 内置的 `InMemoryConfigProvider` 作为唯一配置源，将静态配置（来自 `appsettings.json`）和动态配置（来自 API 调用或客户端注册）统一管理：

```
启动时：
  appsettings.json (ReverseProxy 节) → InMemoryConfigProvider
  gateway-dynamic.json (持久化文件)   → 合并到 InMemoryConfigProvider

运行时：
  API 调用 / 客户端注册 → DynamicYarpConfigService → InMemoryConfigProvider.Update()
                                                      → 持久化到 gateway-dynamic.json
```

#### 双层配置追踪

系统区分两种配置来源：

- **静态配置 (Source = "config")**：来自 `appsettings.json`，启动时加载
- **动态配置 (Source = "dynamic" / "auto-register" / "dashboard")**：来自运行时操作

每个路由和集群都记录 `CreatedAt`、`CreatedBy`、`Source` 元数据，在 Dashboard 中清晰标注。

#### 配置持久化

所有动态变更自动持久化到 `gateway-dynamic.json`（路径可通过 `Gateway:DynamicConfigPath` 配置），网关重启后自动恢复。同时支持**配置快照和回滚**机制——每次 Dashboard 操作前自动保存快照，可一键回退到任意历史版本。

#### RESTful API

`GatewayConfigController` (`api/gateway`) 提供完整的 CRUD 接口：

| 方法 | 路由 | 功能 |
|------|------|------|
| POST | `/api/gateway/register-route` | 注册或更新路由 + 集群 |
| DELETE | `/api/gateway/{routeName}?clientIp=` | 删除路由（支持 IP 隔离模式） |
| GET | `/api/gateway/routes` | 查询所有路由 |
| GET | `/api/gateway/dynamic-config` | 查询动态配置（含元数据） |
| PUT | `/api/gateway/routes/{routeId}` | 更新路由配置 |
| POST | `/api/gateway/clusters` | 创建集群 |
| PUT | `/api/gateway/clusters/{clusterId}` | 更新集群 |
| DELETE | `/api/gateway/clusters/{clusterId}` | 删除集群 |
| GET | `/api/gateway/ping` | 健康检查 |

所有操作都是**线程安全**的（通过 `lock` 保护），确保并发调用的一致性。

### 二、客户端自动注册

微服务接入网关通常需要手动在网关配置路由和集群地址。Aneiang.Yarp.Client 实现了**服务启动时自动注册、关闭时自动注销**的能力。

#### 一行代码接入

```csharp
// Program.cs
builder.Services.AddAneiangYarpClient();
```

然后在 `appsettings.json` 中只需配置网关地址：

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://192.168.1.100:5000"
    }
  }
}
```

#### 智能默认值

`RegistrationOptionsResolver` 会自动推断缺失的配置：

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `RouteName` | 入口程序集名称 | 如 `SampleLocalService` |
| `ClusterName` | 同 RouteName | 集群与路由同名 |
| `MatchPath` | `/{**catch-all}` | 匹配所有路径 |
| `DestinationAddress` | Kestrel 绑定地址 | 自动检测，`localhost` 自动替换为局域网 IP |
| `Order` | 50 | 路由优先级 |

自动将 `localhost` / `127.0.0.1` 替换为局域网 IP，确保网关能正确转发到本地服务。

#### 生命周期管理

通过 `GatewayRegistrationHostedService`（`IHostedService`）实现：

- **启动时**：调用 `POST /api/gateway/register-route` 注册路由
- **关闭时**：调用 `DELETE /api/gateway/{routeName}` 注销路由

优雅关闭机制确保服务下线时网关立即感知，避免请求转发到已停止的实例。

#### Kestrel 自动配置

`UseYarpKestrelAutoConfig()` 扩展方法可以自动检测 Kestrel 的监听地址，无需手动指定 `DestinationAddress`。它通过 `KestrelAutoConfigService` 解析 `IServer` 的Features获取实际监听地址。

### 三、IP 隔离负载均衡

这是 Aneiang.Yarp 最有特色的功能之一，专门解决**开发环境多实例调试**的痛点。

#### 场景

多个开发者同时开发同一个微服务，各自在本地启动实例。传统方案是每个开发者用不同路径前缀（如 `/api/user-dev1/`、`/api/user-dev2/`），前端需要频繁切换，体验很差。

#### 方案

启用 IP 隔离后，所有开发者使用**同一个路由路径**，网关根据请求来源 IP 自动路由到对应开发者的实例：

```
开发者 A (192.168.1.10) → POST /api/user → 网关 → 192.168.1.10:5001
开发者 B (192.168.1.20) → POST /api/user → 网关 → 192.168.1.20:5001
其他请求                  → POST /api/user → 网关 → 第一个可用实例
```

**前端完全无感知**，开发者无需修改任何前端代码。

#### 实现原理

1. 客户端注册时设置 `UseIpIsolation: true`，注册请求携带客户端 IP
2. 网关在同一个集群中创建多个 Destination，每个 Destination 的 key 为 `ip-{address}`，Metadata 中标记 `ClientIp`
3. 自定义负载均衡策略 `IpBasedLoadBalancingPolicy` 根据请求来源 IP 匹配对应 Destination
4. 找不到匹配时回退到第一个可用实例

```csharp
// 客户端 appsettings.json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://localhost:5000",
      "RouteName": "sample-local-service",
      "MatchPath": "/api/local-service/{**catch-all}",
      "UseIpIsolation": true
    }
  }
}
```

`IpBasedLoadBalancingPolicy` 的 IP 获取逻辑支持代理场景，优先读取 `X-Forwarded-For` 头。

### 四、可视化 Dashboard

`Aneiang.Yarp.Dashboard` 提供了一个开箱即用的 Web 管理面板。

#### 一行代码启用

```csharp
// 注册服务
builder.Services.AddAneiangYarpDashboard();

// 注册中间件（在 UseRouting 之后、MapReverseProxy 之前）
app.UseAneiangYarpDashboard();
```

#### 功能概览

| 功能 | 说明 |
|------|------|
| 集群管理 | 查看/创建/修改/删除/重命名集群，管理 Destination 列表 |
| 路由管理 | 查看所有路由配置，编辑匹配路径、优先级、Transforms |
| 配置导入导出 | 导出标准 YARP 格式配置，导入配置并验证格式 |
| 配置快照回滚 | 每次操作自动保存快照，支持一键回滚到历史版本 |
| 实时日志 | 捕获 YARP 代理日志，查看请求路径、方法、状态码、耗时等 |
| 多语言 | 支持中文/英文，运行时可切换 |

#### 认证方式

Dashboard 支持 4 种认证模式：

| 模式 | 说明 |
|------|------|
| `None` | 无认证（开发环境用） |
| `ApiKey` | 通过 `X-Api-Key` 请求头或 `api-key` 查询参数鉴权 |
| `CustomJwt` | 自定义用户名 + 密码，JWT Cookie 认证 |
| `DefaultJwt` | 固定用户名 `admin` + 自定义密码，最常用的模式 |

```json
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "demo123"
    }
  }
}
```

> JWT Secret 未配置时会自动生成（重启后失效）。生产环境建议手动配置 `JwtSecret`。

#### Dashboard 配置项

```csharp
// DashboardOptions 完整配置
{
  "Gateway": {
    "Dashboard": {
      // 基础配置
      "RoutePrefix": "apigateway",           // Dashboard 路由前缀，默认 apigateway
      "AuthMode": "DefaultJwt",              // 认证模式
      "JwtPassword": "your-password",        // JWT 密码
      "JwtSecret": "your-secret-key",        // JWT 签名密钥（生产环境必配）
      "Locale": "zh-CN",                     // 默认语言：zh-CN / en-US

      // 日志控制
      "EnableProxyLogging": true,            // 是否启用代理日志
      "EnableLogSampling": false,            // 是否启用日志采样
      "LogSamplingRate": 1.0,                // 采样率 0.0~1.0
      "LogErrorsOnly": false,                // 仅记录错误请求 (4xx/5xx)
      "LogMaxBodyLength": 8192,              // 请求/响应体最大记录长度
      "LogHeaderBlacklist": ["Authorization", "Cookie"],  // 需脱敏的请求头
      "LogJsonFieldSanitizeList": ["password", "token"]    // 需脱敏的 JSON 字段
    }
  }
}
```

#### 配置管理 API

Dashboard 额外提供了 `ConfigManagementController`（`apigateway/api/config`），支持高级配置管理：

| 方法 | 路由 | 功能 |
|------|------|------|
| GET | `/apigateway/api/config/export` | 导出完整配置（YARP 标准格式） |
| POST | `/apigateway/api/config/import` | 导入配置（含格式验证） |
| PUT | `/apigateway/api/config/clusters/{id}` | 保存集群（自动创建快照） |
| DELETE | `/apigateway/api/config/clusters/{id}` | 删除集群（自动创建快照） |
| PUT | `/apigateway/api/config/clusters/{id}/rename` | 重命名集群（原子操作，自动更新引用路由） |
| PUT | `/apigateway/api/config/routes/{id}` | 保存路由（自动创建快照） |
| DELETE | `/apigateway/api/config/routes/{id}` | 删除路由 |
| GET | `/apigateway/api/config/history` | 获取配置变更历史 |
| POST | `/apigateway/api/config/rollback/{versionId}` | 回滚到指定版本 |
| POST | `/apigateway/api/config/validate` | 验证配置格式 |

### 五、API 鉴权

网关管理 API（`GatewayConfigController`）支持独立的鉴权机制，防止未授权的动态路由操作：

```csharp
builder.Services.AddGatewayApiAuth();
```

支持两种模式：

| 模式 | 说明 | 配置 |
|------|------|------|
| `BasicAuth` | HTTP Basic 认证 | `Username` + `Password` |
| `ApiKey` | API Key 认证 | 通过 `X-Api-Key` 头传递 |

```json
{
  "Gateway": {
    "ApiAuth": {
      "Mode": "BasicAuth",
      "Username": "admin",
      "Password": "secure-password"
    }
  }
}
```

**智能推断**：如果未配置 `Gateway:ApiAuth`，但配置了 `Gateway:Dashboard` 的 JWT 密码，系统会自动推断出 BasicAuth 凭据（用户名 `admin`，密码使用 JWT 密码），无需重复配置。

---

## 快速开始

### 1. 创建网关项目

```bash
dotnet new web -n MyGateway
cd MyGateway
dotnet add package Aneiang.Yarp
dotnet add package Aneiang.Yarp.Dashboard
```

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 一行代码启用网关（自动加载 appsettings.json 中的路由配置）
builder.Services.AddAneiangYarp();

// 一行代码启用 Dashboard
builder.Services.AddAneiangYarpDashboard();

var app = builder.Build();
app.UseRouting();
app.UseAneiangYarpDashboard();
app.MapControllers();
app.MapReverseProxy();
app.Run();
```

```json
// appsettings.json
{
  "ReverseProxy": {
    "Routes": {
      "my-service": {
        "ClusterId": "my-service",
        "Match": { "Path": "/api/my-service/{**catchAll}" }
      }
    },
    "Clusters": {
      "my-service": {
        "Destinations": {
          "d1": { "Address": "http://localhost:5001" }
        }
      }
    }
  },
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "admin123"
    }
  }
}
```

### 2. 创建微服务项目

```bash
dotnet new web -n MyService
cd MyService
dotnet add package Aneiang.Yarp.Client
```

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 一行代码，启动时自动注册到网关
builder.Services.AddAneiangYarpClient();

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
      "GatewayUrl": "http://localhost:5000"
    }
  }
}
```

仅此而已。微服务启动时会自动将自己的地址注册到网关，关闭时自动注销。

### 3. 启动验证

```
╔══════════════════════════════════════════════════╗
║  Internal gateway is running                     ║
║  Dashboard:           /apigateway                ║
║  Login:               /apigateway/login          ║
║  Credentials:         admin / admin123           ║
╚══════════════════════════════════════════════════╝
```

访问 `/apigateway` 即可打开 Dashboard 管理面板。

---

## 架构设计要点

### 配置双写机制

`DynamicYarpConfigService` 维护两份数据的同步：

```
InMemoryConfigProvider (YARP 运行时)
        ↕ lock 保护，原子更新
GatewayDynamicConfig   (持久化模型，含元数据)
        ↓
gateway-dynamic.json   (文件持久化)
```

- 所有写操作在 `lock` 内完成，保证线程安全
- YARP 侧使用 `InMemoryConfigProvider.Update()` 触发配置变更通知
- 持久化侧使用 `DynamicConfigPersistenceService` 写入 JSON 文件
- 启动时先加载持久化配置，再叠加静态配置，最后合并到 YARP

### HostedService 生命周期

```
GatewayRegistrationHostedService
  ├── StartAsync → GatewayAutoRegistrationClient.RegisterAsync()
  │                  → POST /api/gateway/register-route
  └── StopAsync  → GatewayAutoRegistrationClient.UnregisterAsync()
                   → DELETE /api/gateway/{routeName}
```

客户端包对外暴露 `AddAneiangYarpClient()`（含 HostedService），网关内部使用 `AddAneiangYarpClientInternal()`（不含 HostedService，仅注册 DI 服务）。

### 日志采集管道

Dashboard 的日志采集不依赖任何第三方日志框架，通过 YARP 的 EventSource + 自定义 Transform 实现：

```
YarpRequestCaptureMiddleware  → 捕获请求基本信息（路径、方法）
DownstreamCaptureTransform    → 捕获下游响应信息（状态码、耗时）
YarpEventSourceListener       → 通过 DiagnosticListener 监听 YARP 内部事件
ProxyLogStore                 → 内存存储（环形缓冲），按时间窗口清理
```

支持日志采样、错误过滤、路由黑白名单、Header 脱敏、JSON 字段脱敏等精细化控制。

---

## 总结

Aneiang.Yarp 在 YARP 之上封装了三个层次的增强能力：

| 层次 | 包 | 解决的问题 |
|------|-----|-----------|
| 网关核心 | `Aneiang.Yarp` | 动态路由管理、配置持久化、API 鉴权、IP 隔离负载均衡 |
| 可视化 | `Aneiang.Yarp.Dashboard` | 集群/路由管理面板、配置导入导出、快照回滚、实时日志 |
| 客户端 | `Aneiang.Yarp.Client` | 一行代码自动注册/注销、智能默认值、Kestrel 地址自动检测 |

它的核心理念是**开箱即用**——通过合理的默认值和最少的配置，让开发者快速搭建起一个功能完整的 API 网关，同时保留足够的灵活性应对复杂场景。

- GitHub: https://github.com/aneiang/Aneiang.Yarp
- 协议: MIT
