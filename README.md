# Aneiang.Yarp

基于 [YARP (Yet Another Reverse Proxy)](https://github.com/microsoft/reverse-proxy) 的**动态路由网关管理库**，提供动态路由注册、服务自动发现、实时监控仪表盘，同时完整保留 YARP 反向代理的全部能力（负载均衡、健康检查、路由转换、会话保持等）。

### 核心能力

| 能力 | 说明 |
|------|------|
| **动态路由注册** | Gateway 提供 REST API，允许客户端运行时注册/更新/注销反向代理路由 |
| **自动注册客户端** | 本地服务启动时自动向网关注册路由，关闭时自动注销——**一行代码**搞定 |
| **一键式 API** | `AddAneiangYarp()` / `AddAneiangYarpClient()` 一行搭建网关或客户端 |
| **实例隔离** | 多人调试同一服务时自动隔离 routing 命名空间，互不干扰 |
| **高度可定制** | 支持配置优先级：代码 > 环境变量 > 配置文件；组件级 API 精细控制 |
| **仪表盘（可选）** | 实时查看集群、路由、健康状态、YARP 日志摘要 |
| **智能默认值** | `routeName` 取程序集名、`matchPath` 默认全路径转发、`destinationAddress` 自动从 Kestrel 获取，`localhost` 自动解析为内网 IP |

## 项目结构

```
Aneiang.Yarp/
├── src/
│   ├── Aneiang.Yarp/                  # 基础库：动态路由 + 自动注册客户端
│   │   ├── Controllers/
│   │   │   └── GatewayConfigController.cs   # 网关侧 REST API
│   │   ├── Extensions/
│   │   │   └── YarpServiceCollectionExtensions.cs  # DI 注册扩展
│   │   ├── Models/
│   │   │   ├── RegisterRouteRequest.cs      # 注册请求 DTO
│   │   │   └── GatewayRegistrationOptions.cs  # 配置选项（智能默认值）
│   │   └── Services/
│   │       ├── DynamicYarpConfigService.cs        # 动态路由管理
│   │       ├── GatewayAutoRegistrationClient.cs   # 客户端注册服务
│   │       └── GatewayRegistrationHostedService.cs # 托管服务（自动注册/注销）
│   │
│   └── Aneiang.Yarp.Dashboard/         # 仪表盘库（可选）
│       ├── Controllers/
│       │   └── DashboardController.cs     # 仪表盘 API
│       ├── Views/
│       │   ├── Dashboard/Index.cshtml     # 仪表盘 UI
│       │   ├── Dashboard/Login.cshtml      # 登录页面
│       │   └── Shared/_DashboardLayout.cshtml # 布局页
│       ├── Services/
│       │   ├── ProxyLogStore.cs            # 日志环形缓冲区
│       │   └── ProxyLogProvider.cs         # YARP 日志捕获
│       ├── Models/
│       │   ├── DashboardOptions.cs         # 仪表盘配置选项
│       │   └── LogEntry.cs                 # 日志条目模型
│       └── Extensions/
│           └── YarpServiceCollectionExtensions.cs  # DI 注册扩展
│
└── samples/
    ├── SampleGateway/          # 网关项目示例
    └── SampleLocalService/     # 本地服务（客户端）示例
```

### NuGet 包

| 包名 | 说明 | 必需 |
|------|------|------|
| `Aneiang.Yarp` | 基础库：动态路由 + 自动注册客户端 | ✅ |
| `Aneiang.Yarp.Dashboard` | 仪表盘：监控运维 UI | 可选 |

- **目标框架**：`net8.0` / `net9.0`
- **YARP 版本**：`2.3.0`

---

## 快速开始

### ① 搭建内网网关（Gateway 角色）

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ⭐ 一行搭建网关（自动加载 ReverseProxy 静态路由 + 动态路由管理）
builder.Services.AddAneiangYarp();

// 可选：添加仪表盘
builder.Services.AddAneiangYarpDashboard();

var app = builder.Build();

app.UseRouting();
app.MapControllers();         // GatewayConfigController API
app.MapReverseProxy();        // YARP 反向代理

app.Run();
```

**`appsettings.json`**（静态路由可选）：

```json
{
  "ReverseProxy": {
    "Routes": {
      "my-api": {
        "ClusterId": "my-api-cluster",
        "Match": { "Path": "/api/stable/{**catch-all}" }
      }
    },
    "Clusters": {
      "my-api-cluster": {
        "Destinations": {
          "d1": { "Address": "http://internal-server:8080/" }
        }
      }
    }
  }
}
```

### ② 本地服务接入（Client 角色）

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ⭐ 一行搞定：启动自动注册 → 关闭自动注销
builder.Services.AddAneiangYarpClient();

// 你的业务代码
builder.Services.AddControllers();

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.Run();
```

**`appsettings.json`**（最少只需一行）：

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://192.168.1.100:5000"
    }
  }
}
```

> **智能默认值一览：**
> 
> | 字段 | 默认值 | 来源 |
> |------|--------|------|
> | `RouteName` | 入口程序集名称 | `Assembly.GetEntryAssembly()` |
> | `ClusterName` | 同 `RouteName` | — |
> | `MatchPath` | `/{**catch-all}` | — |
> | `DestinationAddress` | Kestrel 绑定地址 | `Urls` 配置 |
> | `Order` | `50` | — |
> | `AutoResolveIp` | `true` | `localhost` → 内网 IP |
> | `TimeoutSeconds` | `5` | — |
> | `Enabled` | 有 `GatewayUrl` 即启用 | 自动判断 |
> | `InstanceIsolation` | `true` | 开启 |
> | `InstanceId` | `Environment.MachineName` | 本机主机名 |

---

## API 设计原则：简洁分层，按需组合

```
  一站式网关              客户端自动注册           可选增强
  (1 行)                 (1 行)                  (1 行)
  ────────               ──────────              ────────
  AddAneiangYarp()       AddAneiangYarpClient()  AddAneiangYarpDashboard()
                                                  AddGatewayApiAuth()
```

### ⭐ 一站式 API

| 方法 | 用途 | 自动注册的服务 |
|------|------|---------------|
| `AddAneiangYarp()` | 搭建网关 | `AddReverseProxy` + `InMemoryConfigProvider` + `DynamicYarpConfigService` + `AddControllersWithViews` + `AddHttpClient` + `GatewayAutoRegistrationClient` |
| `AddAneiangYarpClient()` | 客户端接入 | `AddHttpClient` + `GatewayAutoRegistrationClient` + `GatewayRegistrationHostedService`（自动注册/注销） |

### 🔧 可定制 API

```csharp
// 客户端：自定义注册选项（优先级：代码 > 环境变量 > 配置文件）
builder.Services.AddAneiangYarpClient(options =>
{
    options.GatewayUrl = "http://192.168.1.100:5000";
    options.MatchPath = "/api/users/{**catch-all}";
    options.Order = 10;
    options.AutoResolveIp = true;
    options.TimeoutSeconds = 10;
});

// 网关：自定义 YARP 管道（transforms, service discovery 等）
builder.Services.AddAneiangYarp(proxy =>
{
    proxy.AddTransforms<MyCustomTransform>();
});

// 网关 + 客户端：网关也注册到上级网关
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpClient(o => o.GatewayUrl = "http://upstream-gateway:5000");
```

### 🛠 精细化 API

```csharp
// 完全手动控制
builder.Services.AddReverseProxy();
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpDashboard();
builder.Services.AddGatewayApiAuth();  // 保护 GatewayConfigController API
```

---

## 配置参考

### `Gateway:Registration` 节

所有字段均为可选（除 `GatewayUrl` 外），缺失时使用智能默认值。

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
      "InstanceId": "john"
    }
  }
}
```

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `GatewayUrl` | `string` | ✅ | — | 网关服务地址 |
| `RouteName` | `string` | — | 程序集名 | 路由唯一标识 |
| `ClusterName` | `string` | — | 同 RouteName | 集群名称 |
| `MatchPath` | `string` | — | `/{**catch-all}` | 路径匹配模板 |
| `DestinationAddress` | `string` | — | Kestrel 绑定地址 | 本机目标地址 |
| `Order` | `int` | — | `50` | 路由优先级（越小越优先） |
| `AutoResolveIp` | `bool` | — | `true` | 自动 localhost → 内网 IP |
| `TimeoutSeconds` | `int` | — | `5` | HTTP 请求超时（秒） |
| `Enabled` | `bool` | — | 有 GatewayUrl 即 true | 是否启用 |
| `InstanceIsolation` | `bool` | — | `true` | 开启实例隔离 |
| `InstanceId` | `string` | — | `Environment.MachineName` | 自定义实例标识 |
| `InstancePrefixFormat` | `string` | — | `{instanceId}` | 路径前缀格式模板 |

**配置优先级：代码 `options => {}` > 环境变量 `Gateway__Registration__GatewayUrl` > 配置文件 `appsettings.json`**

### `Gateway:Dashboard` 节

仪表盘配置，绑定配置节 `"Gateway": { "Dashboard": { ... } }`。

```json
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "demo123",
      "RoutePrefix": "apigateway"
    }
  }
}
```

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `AuthMode` | `string` | — | `None` | 鉴权模式：`None` / `ApiKey` / `CustomJwt` / `DefaultJwt` |
| `ApiKey` | `string` | — | — | API Key 值（仅 `ApiKey` 模式） |
| `ApiKeyHeaderName` | `string` | — | `X-Api-Key` | API Key 请求头名称 |
| `JwtSecret` | `string` | — | 随机生成 | JWT 签名密钥（不设置则重启后令牌失效） |
| `JwtUsername` | `string` | — | — | 登录用户名（仅 `CustomJwt` 模式） |
| `JwtPassword` | `string` | — | — | 登录密码（`CustomJwt` / `DefaultJwt` 均需设置） |
| `RoutePrefix` | `string` | — | `apigateway` | 仪表盘路由前缀 |

**鉴权模式说明：**

| 模式 | 验证方式 | 适用场景 |
|------|----------|----------|
| `None` | 不验证 | 内网调试、开发环境 |
| `ApiKey` | 请求头 `X-Api-Key` 或查询参数 `api-key` | 服务间调用、自动化工具 |
| `CustomJwt` | 自定义用户名+密码 → JWT token | 生产环境、多用户管理 |
| `DefaultJwt` | 固定用户名 `admin` + 配置文件密码 → JWT token | 快速启用安全访问 |

JWT 令牌默认有效期 **8 小时**，通过 `POST /apigateway/login` 获取，支持 `Authorization: Bearer` 请求头或 `dashboard_token` Cookie。

### `ReverseProxy` 节

标准 YARP 配置，详见 [YARP 官方文档](https://microsoft.github.io/reverse-proxy/articles/config-files.html)。支持两种 `Match` 写法：

```json
{
  "ReverseProxy": {
    "Routes": {
      "my-route": {
        "ClusterId": "my-cluster",
        "Match": "/api/test/{**catch-all}"        // 简化写法
        // 或标准写法：
        // "Match": { "Path": "/api/test/{**catch-all}", "Methods": ["GET"] }
      }
    },
    "Clusters": {
      "my-cluster": {
        "Destinations": {
          "d1": { "Address": "http://localhost:5000/" }
        },
        "LoadBalancingPolicy": "PowerOfTwoChoices"  // 可选
      }
    }
  }
}
```

---

## API 参考

### 网关侧 API（`/api/gateway`）

这些端点由 `GatewayConfigController` 提供，运行在网关上，供客户端远程调用。

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/gateway/register-route` | `POST` | 注册/更新路由（含数据验证，支持 Transforms） |
| `/api/gateway/{routeName}` | `DELETE` | 注销路由（无关联路由时自动清理集群） |
| `/api/gateway/routes` | `GET` | 查询所有已注册路由 |
| `/api/gateway/ping` | `GET` | 健康检查 |

**注册路由请求体：**

```json
{
  "routeName": "my-service",
  "clusterName": "my-service-cluster",
  "matchPath": "/api/my-service/{**catch-all}",
  "destinationAddress": "http://192.168.1.101:5001",
  "order": 50,
  "transforms": [
    { "PathSet": "/api/backend/{**catch-all}" },
    { "RequestHeader": "X-Forwarded-By", "Set": "gateway" }
  ]
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `routeName` | `string` | ✅ | 路由唯一标识（≤200 字符） |
| `clusterName` | `string` | ✅ | 集群名称（≤200 字符） |
| `matchPath` | `string` | ✅ | 路径匹配模板 |
| `destinationAddress` | `string` | ✅ | 目标 URL |
| `order` | `int` | — | 优先级（默认 50） |
| `transforms` | `array` | — | 路由转换规则（见下方示例） |

**响应格式（所有端点统一）：**

```json
// 成功
{ "code": 200, "info": "路由 'my-service' 注册成功" }

// 带数据
{ "code": 200, "data": [{ "routeId": "...", "clusterId": "...", "path": "...", "order": 50 }] }

// 失败
{ "code": 400, "info": "路由名称不能为空; 目标地址必须是有效的 URL" }
```

### 仪表盘 API（`/apigateway`）

| 端点 | 说明 |
|------|------|
| `GET /apigateway` | 仪表盘首页（HTML） |
| `GET /apigateway/login` | 登录页面（HTML） |
| `POST /apigateway/login` | 登录接口（返回 JWT token） |
| `GET /apigateway/info` | 网关运行信息（版本/环境/运行时间/内存） |
| `GET /apigateway/clusters` | 集群状态（目标列表/健康/不健康/未知） |
| `GET /apigateway/routes` | 路由配置列表（路径/方法/优先级/Transforms） |
| `GET /apigateway/logs` | 最近 YARP 代理日志 |
| `DELETE /apigateway/logs` | 清空日志缓冲区 |

### 扩展方法

| 命名空间 | 方法 | 说明 |
|----------|------|------|
| `Aneiang.Yarp.Extensions` | `AddAneiangYarp()` | ⭐ 一站式搭建网关（含 YARP 反向代理、静态/动态路由管理、Controllers、HttpClient） |
| `Aneiang.Yarp.Extensions` | `AddAneiangYarp(Action<IReverseProxyBuilder>)` | 网关 + YARP 管道定制 |
| `Aneiang.Yarp.Extensions` | `AddAneiangYarpClient()` | ⭐ 客户端自动注册/注销 |
| `Aneiang.Yarp.Extensions` | `AddAneiangYarpClient(Action<GatewayRegistrationOptions>)` | 客户端 + 自定义注册选项 |
| `Aneiang.Yarp.Extensions` | `AddGatewayApiAuth()` | 网关配置 API 授权保护 |
| `Aneiang.Yarp.Dashboard.Extensions` | `AddAneiangYarpDashboard()` | 仪表盘（默认无授权） |
| `Aneiang.Yarp.Dashboard.Extensions` | `AddAneiangYarpDashboard(Action<DashboardOptions>)` | 仪表盘 + 自定义选项 |

---

## 高级用法

### 1. 多级网关链

```
外网 Gateway → 内网 Gateway → 本机调试服务
```

```csharp
// 内网网关同时注册到外网网关
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpClient(o =>
{
    o.GatewayUrl = "http://outer-gateway:5000";
});
```

### 2. 自定义中间件管道

网关项目中可以自由插入中间件，YARP 转发前会经过完整管道：

```csharp
var app = builder.Build();

app.UseAuthentication();       // 认证
app.UseAuthorization();        // 授权
app.UseRequestLogging();       // 日志
app.UseRateLimiter();          // 限流

app.UseRouting();
app.MapControllers();
app.MapReverseProxy();         // YARP 必须放最后
```

### 3. 根据环境切换配置

```json
// appsettings.Development.json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://localhost:5000",
      "MatchPath": "/api/dev/{**catch-all}"
    }
  }
}

// appsettings.Production.json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://gateway.internal:5000"
    }
  }
}
```

### 4. 手动 API 调用（绕过自动注册）

```bash
# 注册
curl -X POST http://gateway:5000/api/gateway/register-route \
  -H "Content-Type: application/json" \
  -d '{
    "routeName": "manual-route",
    "clusterName": "manual-cluster",
    "matchPath": "/api/manual/{**catch-all}",
    "destinationAddress": "http://192.168.1.101:5001",
    "order": 50
  }'

# 查询
curl http://gateway:5000/api/gateway/routes

# 注销
curl -X DELETE http://gateway:5000/api/gateway/manual-route
```

### 5. 内网 IP 自动解析机制

当 `DestinationAddress` 包含 `localhost` / `127.0.0.1` / `0.0.0.0` 时，会自动解析为本机内网 IPv4 地址：

```
配置值:   http://localhost:5001
解析后:   http://192.168.1.101:5001  （本机内网 IP）
```

这样网关才能从内网访问到本机。可通过 `AutoResolveIp = false` 关闭。

### 6. 多人协作：实例隔离模式

**实例隔离默认已开启**，自动将本机标识嵌入路由名称和 URL 路径中，实现完全隔离：

**隔离效果（假设 DevA 在 `PC-JOHN`，DevB 在 `PC-JANE`）：**

| 维度 | 隔离后（互不影响） |
|------|-------------------|
| routeName | `my-service-PC-JOHN` / `my-service-PC-JANE` |
| clusterName | `my-service-cluster-PC-JOHN` / `my-service-cluster-PC-JANE` |
| matchPath | `/PC-JOHN/api/{**catch-all}` / `/PC-JANE/api/{**catch-all}` |

**测试时各自使用隔离后的路径：**

```bash
# DevA 测试（请求经 PC-JOHN → 本机 localhost:5001）
curl http://gateway:5000/PC-JOHN/api/users

# DevB 测试（请求经 PC-JANE → 本机 localhost:5001）
curl http://gateway:5000/PC-JANE/api/users
```

> 如果一人调试、不需要实例隔离，可以显式关闭：
> ```csharp
> builder.Services.AddAneiangYarpClient(options => { options.InstanceIsolation = false; });
> ```

#### 自定义实例标识

```csharp
// 方式 1：代码内指定
builder.Services.AddAneiangYarpClient(options =>
{
    options.InstanceId = "dev-john";              // 简短的别名
    options.InstancePrefixFormat = "dev-{instanceId}";  // 路径前缀格式
});

// 方式 2：配置文件（适合同项目不同人，各自改 appsettings）
// { "Gateway": { "Registration": { "InstanceId": "dev-john" } } }
```

#### 路径前缀格式化

`InstancePrefixFormat` 支持占位符替换，定制路径前缀样式：

| 占位符 | 示例值 | 说明 |
|--------|--------|------|
| `{instanceId}` | `john` | 实例 ID |
| `{machineName}` | `PC-JOHN` | 本机主机名 |
| `{userName}` | `john.doe` | 当前用户名 |

配置示例：

```json
{
  "Gateway": {
    "Registration": {
      "InstanceId": "john",
      "InstancePrefixFormat": "dev-{userName}"
    }
  }
}
```

→ routeName: `my-service-dev-john.doe`
→ matchPath: `/dev-john.doe/{**catch-all}`

---

## 示例项目

| 项目 | 说明 | 启动命令 |
|------|------|----------|
| `samples/SampleGateway` | 内网网关示例（含仪表盘） | `dotnet run --project samples/SampleGateway` |
| `samples/SampleLocalService` | 本地服务示例（自动注册到网关） | `dotnet run --project samples/SampleLocalService` |

```bash
# 终端 1：启动网关（localhost:5000）
cd samples/SampleGateway && dotnet run

# 终端 2：启动本地服务（localhost:5001，自动注册到网关）
cd samples/SampleLocalService && dotnet run

# 验证：请求流经网关 → 转发到本地服务
curl http://localhost:5000/api/your-endpoint
```

---

## 依赖

- **.NET** ≥ 8.0
- **[YARP](https://github.com/microsoft/reverse-proxy)** `2.3.0`
- `Aneiang.Yarp.Dashboard` 依赖 `Aneiang.Yarp`

---

## 许可证

[MIT](LICENSE)
