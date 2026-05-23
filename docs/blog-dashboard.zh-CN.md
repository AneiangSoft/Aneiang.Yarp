# [系列 03] 开箱即用的 YARP 管理面板：Aneiang.Yarp.Dashboard 架构与功能全解析

> **Aneiang.Yarp 源码解析系列** — [上一篇：02 - 网关核心模块](./blog-core.zh-CN.md) | [目录](./series-index.zh-CN.md) | [下一篇：04 - IP 隔离负载均衡](./blog-ip-isolation.zh-CN.md)
>
> YARP 本身不提供管理界面，配置变更靠改 JSON + 重启。Aneiang.Yarp.Dashboard 是一个基于 Razor Class Library 的嵌入式管理面板——两行代码启用，不依赖数据库，不依赖前端构建工具，NuGet 装完就能用。

---

## 一、嵌入式架构：Razor Class Library

Dashboard 采用 ASP.NET Core 的 **Razor Class Library (RCL)** 技术，所有前端资源（HTML、CSS、JavaScript）作为嵌入资源打包在 DLL 中：

```
Aneiang.Yarp.Dashboard.dll
  ├── wwwroot/              ← 静态资源（嵌入）
  │   ├── css/
  │   ├── js/               ← 前端逻辑（原生 JS，无框架）
  │   └── lib/              ← 第三方库
  ├── Pages/                ← Razor Pages
  │   ├── Index.cshtml
  │   ├── Clusters.cshtml
  │   ├── Routes.cshtml
  │   ├── Logs.cshtml
  │   └── Login.cshtml
  └── Controllers/          ← API 控制器
      ├── DashboardController.cs
      ├── ConfigManagementController.cs
      ├── AuditLogController.cs
      ├── CircuitBreakerController.cs
      └── WebSocketLogController.cs
```

**为什么不用 SPA 框架？**

- 零前端构建步骤（不需要 Node.js、Webpack、npm）
- 部署简单——一个 DLL 包含所有内容
- 更新方便——升级 NuGet 包，前端自动更新
- 维护成本低——没有前后端分离的版本同步问题

---

## 二、两行代码启用

```csharp
// 注册服务
builder.Services.AddAneiangYarpDashboard();

// 注册中间件（在 UseRouting 之后、MapReverseProxy 之前）
app.UseAneiangYarpDashboard();
```

`AddAneiangYarpDashboard()` 内部完成：

```
AddAneiangYarpDashboard()
  │
  ├── 1. 配置 RazorPages（设 RootDirectory = "/"）
  │
  ├── 2. 注册 DashboardOptions（绑定 Gateway:Dashboard 配置节）
  │
  ├── 3. 注册所有 Controller
  │     ├── DashboardController（页面路由）
  │     ├── ConfigManagementController（配置 CRUD）
  │     ├── AuditLogController（审计日志）
  │     ├── CircuitBreakerController（熔断器）
  │     └── WebSocketLogController（WebSocket 实时日志）
  │
  ├── 4. 注册 DashboardRouteConvention
  │     └── 为所有 Controller 添加 "apigateway" 路由前缀
  │
  ├── 5. 配置认证（None / ApiKey / DefaultJwt / CustomJwt）
  │
  ├── 6. 注册 ProxyLogStore（内存日志存储）
  │
  └── 7. 可选：AuthorizeRequest 自定义授权委托
```

`UseAneiangYarpDashboard()` 注册的中间件链：

```
请求进入
  │
  ├── YarpRequestCaptureMiddleware
  │     └── 捕获经过网关的请求/响应数据（自动跳过 Dashboard 自身请求）
  │
  ├── MapDashboardEndpoints()
  │     └── 映射 Razor Pages + API 端点
  │
  └── MapReverseProxy()
        └── YARP 代理（必须在最后）
```

---

## 三、路由前缀注入：`DashboardRouteConvention`

Dashboard 的所有端点都在 `/{prefix}` 前缀下（默认 `apigateway`）。这是通过 `IApplicationModelConvention` 实现的：

```csharp
internal sealed class DashboardRouteConvention : IApplicationModelConvention
{
    private readonly string _prefix;

    public void Apply(ApplicationModel application)
    {
        foreach (var ctrl in application.Controllers)
        {
            // 只处理 Dashboard 程序集中的 Controller
            if (ctrl.ControllerType.Assembly != typeof(DashboardController).Assembly) continue;

            // 跳过已经有前缀的 Controller（如 ConfigManagementController）
            var controllerRoute = ctrl.Selectors
                .FirstOrDefault(s => s.AttributeRouteModel?.Template != null)?
                .AttributeRouteModel?.Template ?? "";
            if (controllerRoute.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase)) continue;

            // 为每个 Action 添加前缀
            foreach (var action in ctrl.Actions)
            {
                foreach (var selector in action.Selectors)
                {
                    if (selector.AttributeRouteModel == null) continue;
                    var template = selector.AttributeRouteModel.Template ?? "";
                    selector.AttributeRouteModel.Template = template.StartsWith("/")
                        ? _prefix + template
                        : _prefix + "/" + template;
                }
            }
        }
    }
}
```

**为什么用 Convention 而不是在每个 Controller 上写 `[Route("apigateway/...")]`？**

- 前缀可配置（`RoutePrefix` 选项），Convention 自动适配
- Controller 代码更干净，不需要硬编码前缀
- 统一管理，修改一处全局生效

---

## 四、配置管理：`ConfigManagementController`

这是 Dashboard 的核心 API，提供完整的配置 CRUD 和版本管理。

### 集群管理

| 方法 | 端点 | 功能 |
|------|------|------|
| PUT | `/apigateway/api/config/clusters/{id}` | 创建或更新集群（自动创建快照） |
| DELETE | `/apigateway/api/config/clusters/{id}` | 删除集群（检查路由引用，自动创建快照） |
| PUT | `/apigateway/api/config/clusters/{id}/rename` | 重命名集群（原子操作） |

**集群重命名**是最复杂的操作之一——`DynamicYarpConfigService.TryRenameCluster()` 在单次写锁内完成：

```
1. 创建新集群（复制旧集群的所有 Destination 和元数据）
2. 遍历所有引用旧集群名的路由，更新 ClusterId
3. 删除旧集群
4. 通知 YARP 更新
5. 持久化到文件
```

如果任何一步失败，所有变更回滚，确保数据一致性。

### 路由管理

| 方法 | 端点 | 功能 |
|------|------|------|
| PUT | `/apigateway/api/config/routes/{id}` | 创建或更新路由（自动创建快照） |
| DELETE | `/apigateway/api/config/routes/{id}` | 删除路由 |

### 配置导入导出

```http
GET  /apigateway/api/config/export    # 导出标准 YARP 格式配置
POST /apigateway/api/config/import    # 导入配置（校验 + 合并 + 持久化）
POST /apigateway/api/config/validate  # 校验配置格式
```

导出的配置可以直接粘贴到 `appsettings.json` 的 `ReverseProxy` 节中，完全兼容 YARP 标准格式。

### 快照与回滚

```http
GET  /apigateway/api/config/history                  # 获取变更历史
POST /apigateway/api/config/rollback/{versionId}      # 回滚到指定版本
```

**快照机制**：每次 Dashboard 执行写操作（创建/更新/删除/重命名/导入）之前，自动保存当前完整配置快照。回滚时，将指定版本的配置全量替换当前配置——通过 `DynamicYarpConfigService.ReplaceAllConfig()` 实现。

---

## 五、实时日志采集

Dashboard 的日志采集不依赖任何第三方日志框架，通过 YARP 的中间件和 Transform 实现。

### 采集管道

```
请求进入网关
  │
  ├── YarpRequestCaptureMiddleware（请求前）
  │     └── 记录：Method、Path、QueryParams、RequestHeaders、Body
  │
  ├── YARP 反向代理（转发到下游）
  │     │
  │     └── DownstreamCaptureTransform（响应后）
  │           └── 记录：StatusCode、ResponseHeaders、Body、Duration
  │
  └── YarpRequestCaptureMiddleware（响应后）
        └── 组装完整日志条目 → ProxyLogStore
```

**关键设计**：中间件自动跳过 Dashboard 自身的请求（通过路径前缀判断），避免日志污染。

### ProxyLogStore

内存存储，环形缓冲区设计：

- 最大容量可配置
- 按时间窗口自动清理
- 支持 WebSocket 实时推送（`WebSocketLogController`）

### 生产环境安全控制

```json
{
  "Gateway": {
    "Dashboard": {
      "EnableProxyLogging": true,
      "EnableLogSampling": false,
      "LogSamplingRate": 1.0,
      "LogErrorsOnly": false,
      "LogMaxBodyLength": 8192,
      "LogRouteWhitelist": [],
      "LogRouteBlacklist": [],
      "LogHeaderBlacklist": ["Authorization", "Cookie", "Set-Cookie"],
      "LogQueryBlacklist": [],
      "LogJsonFieldSanitizeList": ["password", "token", "secret", "apikey"]
    }
  }
}
```

| 配置项 | 效果 |
|--------|------|
| `EnableLogSampling` + `LogSamplingRate: 0.1` | 只记录 10% 的请求，降低内存和磁盘压力 |
| `LogErrorsOnly: true` | 只记录 4xx/5xx 请求 |
| `LogMaxBodyLength: 4096` | Body 超过 4KB 的截断 |
| `LogHeaderBlacklist` | 指定 Header 值替换为 `***REDACTED***` |
| `LogJsonFieldSanitizeList` | JSON Body 中指定字段值替换为 `***REDACTED***` |
| `LogRouteWhitelist` | 只记录指定路由的日志 |
| `LogRouteBlacklist` | 不记录指定路由的日志 |

---

## 六、四种认证模式

| 模式 | 适合场景 | 配置复杂度 |
|------|---------|-----------|
| `None` | 本地开发 | 零配置 |
| `DefaultJwt` | 个人/小团队 | 配一个密码 |
| `CustomJwt` | 企业项目 | 配用户名 + 密码 |
| `ApiKey` | API 对接 / 脚本调用 | 配一个 API Key |

### DefaultJwt（最常用）

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

- 用户名固定 `admin`
- 密码通过 `JwtPassword` 配置
- JWT Secret 不配则自动生成（重启后失效，生产环境建议手动配置 `JwtSecret`）
- 通过 Cookie 持久化登录状态

### 自定义授权（最高优先级）

```csharp
builder.Services.AddAneiangYarpDashboard(options =>
{
    options.AuthorizeRequest = async (context) =>
    {
        // 接入你自己的认证体系（如公司 SSO）
        return context.User.Identity?.IsAuthenticated == true
            && context.User.IsInRole("GatewayAdmin");
    };
});
```

优先级：`AuthorizeRequest` > `ApiKey` > `JWT` > `None`

---

## 七、多语言支持

Dashboard 内置中英文双语，运行时可切换：

```json
{
  "Gateway": {
    "Dashboard": {
      "Locale": "zh-CN"  // "zh-CN" 或 "en-US"
    }
  }
}
```

前端所有文本通过语言包管理，切换无需重启页面。语言包存储在嵌入式 JavaScript 中，没有额外的翻译文件需要维护。

---

## 八、审计日志 API

`AuditLogController` 提供配置变更的审计查询：

```http
GET /apigateway/api/audit-logs              # 获取所有审计记录
GET /apigateway/api/audit-logs/{id}         # 获取单条记录
```

结合核心模块的 `ConfigChangeAuditLog` 服务，记录每一次配置变更的完整上下文——谁在什么时间做了什么操作，成功还是失败，变更前后的值是什么。

---

## 九、端点一览

### 页面端点

| 端点 | 方法 | 说明 |
|------|------|------|
| `/apigateway` | GET | Dashboard 首页（概览） |
| `/apigateway/login` | GET/POST | 登录页 / 登录验证 |
| `/apigateway/logout` | POST | 登出 |
| `/apigateway/info` | GET | 网关运行信息 |
| `/apigateway/clusters` | GET | 集群列表 |
| `/apigateway/routes` | GET | 路由列表 |
| `/apigateway/logs` | GET/DELETE | 日志查询 / 清空 |
| `/apigateway/auth/status` | GET | 认证状态 |

### API 端点

| 端点 | 说明 |
|------|------|
| `/apigateway/api/config/export` | 导出完整 YARP 配置 |
| `/apigateway/api/config/import` | 导入配置 |
| `/apigateway/api/config/validate` | 校验配置格式 |
| `/apigateway/api/config/history` | 变更历史 |
| `/apigateway/api/config/rollback/{id}` | 回滚 |
| `/apigateway/api/config/routes/{id}` | 路由 CRUD |
| `/apigateway/api/config/clusters/{id}` | 集群 CRUD |
| `/apigateway/api/config/clusters/{id}/rename` | 集群重命名 |
| `/apigateway/api/audit-logs` | 审计日志 |
| `/apigateway/api/circuit-breaker` | 熔断器状态 |
| `/apigateway/api/websocket-logs` | WebSocket 实时日志 |

---

## 十、SampleGateway 示例

```csharp
// samples/SampleGateway/Program.cs
var builder = WebApplication.CreateBuilder(args);

// 一行代码启用网关
builder.Services.AddAneiangYarp();

// 一行代码启用 Dashboard（DefaultJwt 认证，密码 demo123）
builder.Services.AddAneiangYarpDashboard();

var app = builder.Build();
app.UseRouting();
app.UseAneiangYarpDashboard();  // 中间件注册
app.MapControllers();
app.MapReverseProxy();
app.Run();
```

```json
// appsettings.json
{
  "ReverseProxy": {
    "Routes": {
      "AuthServiceSelfRoute": {
        "ClusterId": "AuthServiceCluster",
        "Order": 2,
        "Match": { "Path": "/api/auth-service/{**catchAll}" }
      }
    },
    "Clusters": {
      "AuthServiceCluster": {
        "Destinations": {
          "d1": { "Address": "http://192.168.16.19:20002" },
          "d2": { "Address": "http://192.168.16.19:20003" }
        }
      }
    }
  },
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "demo123"
    }
  }
}
```

启动后访问 `http://localhost:5000/apigateway`，使用 `admin / demo123` 登录。

---

## 设计亮点总结

| 特性 | 设计 | 优势 |
|------|------|------|
| **嵌入式部署** | Razor Class Library | 零前端构建，一个 DLL 搞定 |
| **路由前缀可配** | IApplicationModelConvention | 不硬编码，灵活部署 |
| **配置快照** | 操作前自动保存 | 改坏了一键回滚 |
| **日志采集** | YARP 中间件 + Transform | 不侵入业务代码 |
| **日志脱敏** | Header/Query/JSON 多层脱敏 | 生产环境安全合规 |
| **日志采样** | 可配置采样率 | 控制高流量日志量 |
| **多语言** | 运行时切换 | 国际化友好 |
| **四种认证** | None/ApiKey/JWT/自定义 | 从开发到生产全覆盖 |
| **无外部依赖** | 内存存储 + 文件持久化 | 不需要数据库 |

---

## 项目信息

- **GitHub**: [https://github.com/aneiang/Aneiang.Yarp](https://github.com/aneiang/Aneiang.Yarp)
- **NuGet**: `dotnet add package Aneiang.Yarp.Dashboard`
- **协议**: MIT
- **在线预览**: http://113.45.65.71:8930/apigateway（admin/demo123）

```bash
dotnet add package Aneiang.Yarp.Dashboard
# Program.cs: builder.Services.AddAneiangYarpDashboard();
#            app.UseAneiangYarpDashboard();
```

*（全文完）*

---

> **Aneiang.Yarp 源码解析系列** — [上一篇：02 - 网关核心模块](./blog-core.zh-CN.md) | [目录](./series-index.zh-CN.md) | [下一篇：04 - IP 隔离负载均衡](./blog-ip-isolation.zh-CN.md)
