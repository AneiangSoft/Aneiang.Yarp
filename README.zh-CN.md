<div align="center">![输入图片说明](Logo.png)


**基于 YARP 的全功能 API 网关增强方案 — Dashboard、动态路由、自动注册、IP 隔离**

[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp)
[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard)
[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Client.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Client)
[![YARP](https://img.shields.io/badge/YARP-2.3.0-blue.svg)](https://github.com/microsoft/reverse-proxy)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-purple.svg)](https://dotnet.microsoft.com/)

[English](README.md) | [中文](README.zh-CN.md)

</div>

---

Aneiang.Yarp 是基于 [微软 YARP](https://microsoft.github.io/reverse-proxy/) 2.3.0 构建的 API 网关增强方案，通过三个 NuGet 包解决实际网关使用中的痛点：

| 包 | 用途 | 依赖 YARP |
|----|------|:---:|
| **Aneiang.Yarp** | 网关核心：动态路由、配置持久化、API 鉴权、IP 隔离负载均衡 | 是 |
| **Aneiang.Yarp.Client** | 客户端自动注册：一行代码接入，启动注册、关闭注销 | 否 |
| **Aneiang.Yarp.Dashboard** | 可视化管理面板：集群/路由 CRUD、配置导入导出、快照回滚、实时日志 | 通过核心库 |

```
依赖关系：

Aneiang.Yarp.Dashboard
  └── Aneiang.Yarp
        └── Aneiang.Yarp.Client

客户端服务 → 仅引用 Aneiang.Yarp.Client（不拉入 YARP SDK）
网关服务   → 引用 Aneiang.Yarp + Aneiang.Yarp.Dashboard
```

在线预览：http://113.45.65.71:8930/apigateway &nbsp;&nbsp; admin/demo123

---

## Dashboard 预览

<table>
  <tr>
    <td align="center"><b>集群管理</b></td>
    <td align="center"><b>路由管理</b></td>
  </tr>
  <tr>
    <td><img src="docs/cluster-list.png" alt="集群列表" width="480"/></td>
    <td><img src="docs/route-list.png" alt="路由列表" width="480"/></td>
  </tr>
  <tr>
    <td align="center"><b>JSON 编辑器</b></td>
    <td align="center"><b>请求日志</b></td>
  </tr>
  <tr>
    <td><img src="docs/cluster-create.png" alt="集群编辑" width="480"/></td>
    <td><img src="docs/log-list.png" alt="请求日志" width="480"/></td>
  </tr>
</table>

<div align="center">
<img src="docs/overview.png" alt="项目概览" width="720"/>
</div>

---

## 功能特性

### 动态路由管理

YARP 原生依赖 `appsettings.json` 管理路由，修改需要重启。Aneiang.Yarp 提供完整的运行时管理能力：

- **双层配置源**：静态配置（`appsettings.json`）+ 动态配置（API 调用），统一到 YARP 的 `InMemoryConfigProvider`
- **配置持久化**：所有动态变更自动持久化到 `gateway-dynamic.json`，重启后自动恢复
- **线程安全**：所有操作通过 `lock` 保护，确保并发安全
- **来源追踪**：每个路由/集群记录 `CreatedAt`、`CreatedBy`、`Source` 元数据

RESTful API（`api/gateway`）：

| 方法 | 端点 | 说明 |
|------|------|------|
| POST | `/api/gateway/register-route` | 注册或更新路由 + 集群 |
| DELETE | `/api/gateway/{routeName}?clientIp=` | 删除路由（支持 IP 隔离模式） |
| GET | `/api/gateway/routes` | 查询所有路由 |
| GET | `/api/gateway/dynamic-config` | 查询动态配置（含元数据） |
| PUT | `/api/gateway/routes/{routeId}` | 更新路由 |
| POST | `/api/gateway/clusters` | 创建集群 |
| PUT | `/api/gateway/clusters/{clusterId}` | 更新集群 |
| DELETE | `/api/gateway/clusters/{clusterId}` | 删除集群 |
| GET | `/api/gateway/ping` | 健康检查 |

### 客户端自动注册

微服务一行代码接入网关，启动自动注册、关闭自动注销：

```csharp
builder.Services.AddAneiangYarpClient();
```

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://192.168.1.100:5000"
    }
  }
}
```

**智能默认值** — 只需配 `GatewayUrl`，其他全部自动推断：

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `RouteName` | 入口程序集名称 | 如 `MyService` |
| `ClusterName` | 同 RouteName | |
| `MatchPath` | `/{**catch-all}` | 匹配所有路径 |
| `DestinationAddress` | Kestrel 绑定地址 | 自动检测，`localhost` 自动替换为局域网 IP |
| `Order` | `50` | 路由优先级 |

> `Aneiang.Yarp.Client` **不依赖 YARP SDK**，仅依赖 `Microsoft.AspNetCore.App` 框架引用。下游服务引用干净，不会间接拉入整个 YARP 包。

### IP 隔离负载均衡

专为**多人开发调试**设计的特色功能。多个开发者各自在本地启动同一个服务，网关按客户端 IP 自动路由 — 前端完全无感知。

```
开发者 A (192.168.1.10) → POST /api/user → 网关 → 192.168.1.10:5001
开发者 B (192.168.1.20) → POST /api/user → 网关 → 192.168.1.20:5001
其他请求                  → POST /api/user → 网关 → 第一个可用实例
```

客户端开启 IP 隔离：

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://localhost:5000",
      "RouteName": "my-service",
      "MatchPath": "/api/my-service/{**catch-all}",
      "UseIpIsolation": true
    }
  }
}
```

实现原理：自定义 `IpBasedLoadBalancingPolicy` 根据请求来源 IP（支持 `X-Forwarded-For` 和 `RemoteIpAddress`）匹配对应 Destination 的 Metadata。

### Dashboard 可视化面板

两行代码启用完整的管理界面：

```csharp
builder.Services.AddAneiangYarpDashboard();
// ...
app.UseAneiangYarpDashboard();
```

| 功能 | 说明 |
|------|------|
| 集群 & 路由 CRUD | 表单 + JSON 编辑器，支持 YARP 标准格式 |
| 配置导入导出 | 一键导出/导入标准 YARP 格式配置，含自动校验 |
| 快照与回滚 | 每次操作前自动保存快照，一键回滚到历史版本 |
| 实时日志 | 请求/响应全记录，按路由/状态码/TraceID 过滤 |
| 日志脱敏 | 自动遮蔽敏感 Header、查询参数、JSON 字段 |
| 日志采样 | 按比例采样，控制生产环境日志量 |
| 中英文切换 | 运行时切换语言，无需重启 |
| 多模式认证 | 无认证 / API Key / JWT（默认 & 自定义）/ 自定义委托 |

### API 鉴权

为网关管理 API（`GatewayConfigController`）提供可选的鉴权保护：

```csharp
builder.Services.AddGatewayApiAuth();
```

| 模式 | 说明 |
|------|------|
| `BasicAuth` | HTTP Basic 认证 |
| `ApiKey` | 通过 `X-Api-Key` 请求头认证 |
| `None` | 无保护（默认） |

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

**智能推断**：如果未配置 `Gateway:ApiAuth`，但配置了 `Gateway:Dashboard` 的 JWT 密码，系统自动推断出认证凭据（用户名 `admin`，密码 = JWT 密码），无需重复配置。

---

## 快速开始

### 1. 创建网关

```bash
dotnet new web -n MyGateway
cd MyGateway
dotnet add package Aneiang.Yarp
dotnet add package Aneiang.Yarp.Dashboard
```

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpDashboard();

var app = builder.Build();
app.UseRouting();
app.UseAneiangYarpDashboard();
app.MapControllers();
app.MapReverseProxy();
app.Run();
```

### 2. 创建微服务

```bash
dotnet new web -n MyService
cd MyService
dotnet add package Aneiang.Yarp.Client
```

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);
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

就这样。微服务启动时自动注册到网关，关闭时自动注销。

---

## Dashboard 配置参考

所有配置都在 `Gateway:Dashboard` 下，不配也能跑（全部有默认值）。

```json
{
  "Gateway": {
    "Dashboard": {
      "EnableProxyLogging": true,
      "RoutePrefix": "apigateway",
      "Locale": "zh-CN",

      "AuthMode": "DefaultJwt",
      "JwtPassword": "your-strong-password",
      "JwtUsername": "admin",
      "JwtSecret": "...",
      "ApiKey": "your-api-key",
      "ApiKeyHeaderName": "X-Api-Key",

      "EnableLogSampling": false,
      "LogSamplingRate": 1.0,
      "LogErrorsOnly": false,
      "LogMaxBodyLength": 8192,

      "LogRouteWhitelist": [],
      "LogRouteBlacklist": [],
      "LogHeaderBlacklist": ["Authorization", "Cookie", "Set-Cookie"],
      "LogQueryBlacklist": [],
      "LogJsonFieldSanitizeList": ["password", "token", "secret", "apikey", "api-key"]
    }
  }
}
```

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `EnableProxyLogging` | `true` | 日志总开关 |
| `RoutePrefix` | `"apigateway"` | Dashboard URL 前缀 |
| `Locale` | `"zh-CN"` | 默认语言，运行时可切换（`zh-CN` / `en-US`） |
| `AuthMode` | `None` | `None` / `ApiKey` / `CustomJwt` / `DefaultJwt` |
| `JwtPassword` | null | JWT 登录密码 |
| `JwtUsername` | null | CustomJwt 用户名（DefaultJwt 固定 `admin`） |
| `JwtSecret` | null | JWT 签名密钥，不配则自动生成（重启失效） |
| `ApiKey` | null | API Key 值 |
| `ApiKeyHeaderName` | `"X-Api-Key"` | API Key 的 Header 名 |
| `EnableLogSampling` | `false` | 启用采样 |
| `LogSamplingRate` | `1.0` | 采样率 0.0~1.0 |
| `LogErrorsOnly` | `false` | 只记错误（4xx/5xx） |
| `LogMaxBodyLength` | `8192` | Body 最大长度（字节） |
| `LogRouteWhitelist` | null | 路由白名单 |
| `LogRouteBlacklist` | null | 路由黑名单 |
| `LogHeaderBlacklist` | null | Header 脱敏列表 |
| `LogQueryBlacklist` | null | 查询参数脱敏列表 |
| `LogJsonFieldSanitizeList` | null | JSON 字段脱敏列表 |

### Dashboard 端点

#### 页面 — `/{RoutePrefix}`

| 端点 | 方法 | 说明 |
|------|------|------|
| `/{prefix}` | GET | Dashboard 首页 |
| `/{prefix}/login` | GET/POST | 登录页 / 登录验证 |
| `/{prefix}/logout` | POST | 登出 |
| `/{prefix}/info` | GET | 网关运行信息 |
| `/{prefix}/clusters` | GET | 集群列表 |
| `/{prefix}/routes` | GET | 路由列表 |
| `/{prefix}/logs` | GET/DELETE | 日志查询 / 清空 |
| `/{prefix}/auth/status` | GET | 认证状态 |

#### 配置管理 — `/{RoutePrefix}/api/config`

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/config/export` | GET | 导出完整 YARP 配置 |
| `/api/config/import` | POST | 导入配置（校验 + 快照 + 持久化） |
| `/api/config/validate` | POST | 校验配置格式 |
| `/api/config/history` | GET | 变更历史 |
| `/api/config/rollback/{id}` | POST | 回滚到指定版本 |
| `/api/config/routes/{id}` | PUT/DELETE | 新增更新 / 删除路由 |
| `/api/config/clusters/{id}` | PUT/DELETE | 新增更新 / 删除集群 |
| `/api/config/clusters/{id}/rename` | PUT | 重命名集群 |

---

## 高级用法

<details>
<summary><b>自定义授权 — 接入你自己的认证体系</b></summary>

```csharp
builder.Services.AddAneiangYarpDashboard(options =>
{
    options.AuthorizeRequest = async (context) =>
    {
        // 返回 true = 放行
        return context.User.Identity?.IsAuthenticated == true
            && context.User.IsInRole("GatewayAdmin");
    };
});
```

优先级：`AuthorizeRequest` > `ApiKey` > `JWT` > `None`

</details>

<details>
<summary><b>网关 API 鉴权 — 智能凭据推断</b></summary>

网关配了 Dashboard 认证后，`AddGatewayApiAuth()` 自动读取 JWT 密码作为 API 鉴权凭据：

```csharp
// 网关
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpDashboard();
builder.Services.AddGatewayApiAuth();  // 自动读取 Dashboard JWT 密码
```

客户端无需额外配置。

</details>

<details>
<summary><b>中间件顺序</b></summary>

```csharp
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseRouting();
app.UseAneiangYarpDashboard();  // ← 在 UseRouting 之后
app.MapControllers();
app.MapReverseProxy();           // ← 必须最后
```

中间件职责：捕获 YARP 代理的请求/响应数据，自动跳过 Dashboard 自身请求。

</details>

<details>
<summary><b>生产环境推荐配置</b></summary>

```json
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "very-strong-password-here",
      "JwtSecret": "your-persisted-secret-key",
      "EnableLogSampling": true,
      "LogSamplingRate": 0.1,
      "LogErrorsOnly": true,
      "LogMaxBodyLength": 4096,
      "LogHeaderBlacklist": ["Authorization", "Cookie", "Set-Cookie", "X-Api-Key"],
      "LogJsonFieldSanitizeList": ["password", "token", "secret", "apikey", "creditCard", "ssn"]
    }
  }
}
```

</details>

---

## 示例项目

```bash
# 启动网关（含 Dashboard）
dotnet run --project samples/SampleGateway

# 启动客户端（自动注册到网关）
dotnet run --project samples/SampleLocalService

# 测试
curl http://localhost:5000/api/your-endpoint
```

Dashboard：`/apigateway`，登录：`admin` / `demo123`

---

## NuGet

| 包 | 说明 | 链接 |
|----|------|------|
| **Aneiang.Yarp** | 网关核心：动态路由、IP 隔离、API 鉴权 | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp) |
| **Aneiang.Yarp.Client** | 客户端自动注册（轻量，无 YARP 依赖） | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Client.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Client) |
| **Aneiang.Yarp.Dashboard** | 可视化管理面板：认证、日志、配置管理 | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard) |

**支持** .NET 8.0 / .NET 9.0 · YARP 2.3.0

---

## 许可证

[MIT](LICENSE) — 随便用。

---

<div align="center">

觉得有用？[⭐ Star 一下](https://github.com/aneiang/Aneiang.Yarp) 让更多人看到

</div>
