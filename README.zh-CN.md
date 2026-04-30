# Aneiang.Yarp

<div align="center">

**基于 .NET 的动态路由网关管理库**

[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp)
[![License](https://img.shields.io/github/license/aneiang/Aneiang.Yarp.svg)](LICENSE)
[![YARP Version](https://img.shields.io/badge/YARP-2.3.0-blue.svg)](https://github.com/microsoft/reverse-proxy)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-purple.svg)](https://dotnet.microsoft.com/)

[English](README.md) | [中文](README.zh-CN.md)

</div>

---

基于 [Microsoft YARP](https://github.com/microsoft/reverse-proxy) 构建的强大**动态路由网关管理库**，提供运行时路由注册、服务自动发现、实时监控仪表盘，同时完整保留 YARP 反向代理的全部能力。

## ✨ 核心特性

| 特性 | 说明 |
|------|------|
| 🚀 **动态路由** | REST API 支持运行时注册/更新/注销路由 |
| 🔄 **自动注册** | 服务启动自动注册、关闭自动注销 — **仅需一行代码** *(需要网络互通，主要用于调试)* |
| 🎯 **一键式 API** | `AddAneiangYarp()` / `AddAneiangYarpClient()` 快速搭建 |
| 👥 **实例隔离** | 多人调试时自动隔离路由命名空间，互不干扰 |
| ⚙️ **高度可定制** | 代码 > 环境变量 > 配置文件优先级；组件级精细控制 |
| 📊 **仪表盘（主推）** | 实时查看集群、路由、健康状态和 YARP 日志 |
| 🧠 **智能默认值** | 自动获取程序集名、Kestrel 地址，localhost 自动解析为内网 IP |

## 📦 NuGet 包

| 包名 | 说明 | 链接 |
|------|------|------|
| **Aneiang.Yarp** | 基础库：动态路由 + 自动注册客户端 | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp) |
| **Aneiang.Yarp.Dashboard** | 🌟 **主推产品**：监控运维仪表盘 | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard) |

**环境要求：**
- 目标框架：`.NET 8.0` / `.NET 9.0`
- YARP 版本：`2.3.0`

---

## 🚀 快速开始

### 1️⃣ 搭建网关

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ⭐ 一行搭建网关
builder.Services.AddAneiangYarp();

// 可选：添加仪表盘
builder.Services.AddAneiangYarpDashboard();

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.MapReverseProxy();
app.Run();
```

### 2️⃣ 接入客户端服务

> **注意**：自动注册功能需要客户端服务与网关之间网络互通。此功能主要为**开发和调试场景**设计。

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ⭐ 一行搞定：启动自动注册 → 关闭自动注销
builder.Services.AddAneiangYarpClient();

builder.Services.AddControllers();

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.Run();
```

**`appsettings.json`**（最小配置）：

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://192.168.1.100:5000"
    }
  }
}
```

就这么简单！服务启动时会自动注册到网关，关闭时自动注销。

---

## 📸 仪表盘截图

### 集群状态

![集群列表](docs/cluster-list.png)

### 路由配置

![路由列表](docs/route-list.png)

### 日志查看

![日志列表](docs/log-list.png)

---

## ⚡ 高级用法

<details>
<summary><b>🔗 多级网关链</b></summary>

```csharp
// 内网网关同时注册到外网网关
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpClient(o =>
{
    o.GatewayUrl = "http://outer-gateway:5000";
});
```

</details>

<details>
<summary><b>🛡️ 网关 API 授权保护</b></summary>

使用 BasicAuth 或 ApiKey 保护注册 API：

```csharp
// 从 Dashboard 配置自动检测
builder.Services.AddGatewayApiAuth();

// 或显式配置
builder.Services.AddGatewayApiAuth(o =>
{
    o.Mode = GatewayApiAuthMode.BasicAuth;
    o.Username = "admin";
    o.Password = "admin@2026";
});
```

配置文件方式：
```json
{
  "Gateway": {
    "ApiAuth": {
      "Mode": "BasicAuth",
      "Username": "admin",
      "Password": "admin@2026"
    }
  }
}
```

</details>

<details>
<summary><b>👥 实例隔离（多人协作）</b></summary>

**默认已开启** — 自动将机器标识嵌入路由：

| 维度 | 开发者 A (PC-JOHN) | 开发者 B (PC-JANE) |
|------|-------------------|-------------------|
| routeName | `my-service-PC-JOHN` | `my-service-PC-JANE` |
| matchPath | `/PC-JOHN/api/{**catch-all}` | `/PC-JANE/api/{**catch-all}` |

自定义实例 ID：
```csharp
builder.Services.AddAneiangYarpClient(options =>
{
    options.InstanceId = "dev-john";
    options.InstancePrefixFormat = "dev-{instanceId}";
});
```

如不需要可关闭：
```csharp
options.InstanceIsolation = false;
```

</details>

<details>
<summary><b>🌐 内网 IP 自动解析</b></summary>

当 `DestinationAddress` 包含 `localhost` / `127.0.0.1` / `0.0.0.0` 时：

```
配置值:   http://localhost:5001
解析后:   http://192.168.1.101:5001  （内网 IP）
```

关闭：`AutoResolveIp = false`

</details>

<details>
<summary><b>🔧 自定义中间件管道</b></summary>

```csharp
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRequestLogging();
app.UseRateLimiter();

app.UseRouting();
app.MapControllers();
app.MapReverseProxy();  // 必须放最后
```

</details>

---

## 📖 配置参考

<details>
<summary><b>Gateway:Registration</b> — 完整配置选项</summary>

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://192.168.1.100:5000",
      "RouteName": "my-service",
      "ClusterName": "my-service-cluster",
      "MatchPath": "/api/my-service/{**catch-all}",
      "DestinationAddress": "http://localhost:5001",
      "Order": 50,
      "AutoResolveIp": true,
      "TimeoutSeconds": 10,
      "InstanceIsolation": true,
      "InstanceId": "john",
      "InstancePrefixFormat": "{instanceId}",
      "StripInstancePrefix": true,
      "DownstreamPathPrefix": null,
      "Transforms": [],
      "AuthToken": null,
      "ApiKey": null,
      "BasicAuthUsername": null,
      "BasicAuthPassword": null
    }
  }
}
```

**配置优先级：** 代码 `options => {}` > 环境变量 > `appsettings.json`

</details>

<details>
<summary><b>Gateway:Dashboard</b> — 仪表盘配置</summary>

```json
{
  "Gateway": {
    "Dashboard": {
      "EnableProxyLogging": true,
      "AuthMode": "DefaultJwt",
      "JwtPassword": "demo123",
      "RoutePrefix": "apigateway",
      "Locale": "zh-CN"
    }
  }
}
```

**鉴权模式：** `None` | `ApiKey` | `CustomJwt` | `DefaultJwt`

JWT 令牌默认有效期 **8 小时**。通过 `POST /apigateway/login` 获取。

</details>

<details>
<summary><b>ReverseProxy</b> — 标准 YARP 配置</summary>

```json
{
  "ReverseProxy": {
    "Routes": {
      "my-route": {
        "ClusterId": "my-cluster",
        "Match": "/api/test/{**catch-all}"
      }
    },
    "Clusters": {
      "my-cluster": {
        "Destinations": {
          "d1": { "Address": "http://localhost:5000/" }
        },
        "LoadBalancingPolicy": "PowerOfTwoChoices"
      }
    }
  }
}
```

详见 [YARP 官方文档](https://microsoft.github.io/reverse-proxy/articles/config-files.html)。

</details>

---

## 🔌 API 端点

<details>
<summary><b>网关 API</b> — <code>/api/gateway</code></summary>

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/gateway/register-route` | `POST` | 注册/更新路由 |
| `/api/gateway/{routeName}` | `DELETE` | 注销路由 |
| `/api/gateway/routes` | `GET` | 查询所有路由 |
| `/api/gateway/ping` | `GET` | 健康检查 |

**请求示例：**
```json
{
  "routeName": "my-service",
  "clusterName": "my-service-cluster",
  "matchPath": "/api/my-service/{**catch-all}",
  "destinationAddress": "http://192.168.1.101:5001",
  "order": 50,
  "transforms": [
    { "PathSet": "/api/backend/{**catch-all}" }
  ]
}
```

**响应格式：**
```json
{ "code": 200, "message": "路由注册成功" }
```

</details>

<details>
<summary><b>仪表盘 API</b> — <code>/apigateway</code></summary>

| 端点 | 说明 |
|------|------|
| `GET /apigateway` | 仪表盘首页 |
| `GET /apigateway/login` | 登录页面 |
| `POST /apigateway/login` | 登录接口（返回 JWT 令牌） |
| `GET /apigateway/info` | 网关运行信息 |
| `GET /apigateway/clusters` | 集群状态 |
| `GET /apigateway/routes` | 路由配置 |
| `GET /apigateway/routes/{routeId}` | 路由详情 |
| `GET /apigateway/logs` | 最近 YARP 日志 |
| `DELETE /apigateway/logs` | 清空日志 |

</details>

---

## 📂 项目结构

```
Aneiang.Yarp/
├── src/
│   ├── Aneiang.Yarp/                  # 基础库
│   │   ├── Controllers/               # 网关 REST API
│   │   ├── Extensions/                # DI 注册扩展
│   │   ├── Models/                    # 数据模型和配置选项
│   │   └── Services/                  # 核心服务
│   │
│   └── Aneiang.Yarp.Dashboard/        # 仪表盘库
│       ├── Controllers/               # 仪表盘 API
│       ├── Views/                     # Razor 视图
│       ├── Services/                  # 仪表盘服务
│       ├── Models/                    # 仪表盘模型
│       └── Extensions/                # DI 注册扩展
│
└── samples/
    ├── SampleGateway/                 # 网关示例
    └── SampleLocalService/            # 客户端示例
```

---

## 🧪 示例项目

```bash
# 终端 1：启动网关
dotnet run --project samples/SampleGateway

# 终端 2：启动本地服务（自动注册）
dotnet run --project samples/SampleLocalService

# 测试路由
curl http://localhost:5000/api/your-endpoint
```

**示例网关特性：**
- 仪表盘访问 `/apigateway`（登录：`admin` / `demo123`）
- Serilog 日志
- JWT 认证

---

## 📄 许可证

本项目采用 [MIT 许可证](LICENSE)。

---

<div align="center">

**为 .NET 社区用心打造 ❤️**

如果觉得有用，请 [⭐ 给个 Star](https://github.com/aneiang/Aneiang.Yarp)！

</div>
