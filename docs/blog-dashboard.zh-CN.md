# 开箱即用的 YARP 管理面板：Aneiang.Yarp.Dashboard 架构与功能全解析

> **Aneiang.Yarp 源码解析系列（篇 03）**
> | [上一篇：网关核心模块](./blog-core.zh-CN.md) | [下一篇：IP 隔离负载均衡](./blog-ip-isolation.zh-CN.md) | [系列目录](./series-index.zh-CN.md) |

YARP 本身不提供管理界面，配置变更靠改 JSON + 重启。Aneiang.Yarp.Dashboard 是一个**嵌入式**管理面板 —— 两行代码启用，不依赖数据库，不依赖前端构建工具，NuGet 装完就能用。

**本文你会了解到：**

- Dashboard 如何用 Razor Class Library 打包成单个 DLL
- 路由前缀如何通过 MVC Convention 动态注入
- 日志采集管道的实现（不依赖第三方日志框架）
- 配置快照与回滚机制
- 四种认证模式的切换方式

---

## 嵌入式架构：一个 DLL 包含一切

Dashboard 采用 ASP.NET Core 的 **Razor Class Library (RCL)** 技术：

```
Aneiang.Yarp.Dashboard.dll
  ├── wwwroot/       ← 静态资源（CSS、JS）作为嵌入资源
  ├── Pages/         ← Razor Pages 页面
  └── Controllers/   ← API 控制器
```

**为什么不用 Vue / React 做 SPA？**

- 零前端构建步骤（不需要 Node.js、Webpack、npm）
- 部署简单 —— 一个 DLL 包含所有内容
- 升级方便 —— 升级 NuGet 包，前端自动更新
- 维护成本低 —— 没有前后端版本同步问题

---

## 两行代码启用

```csharp
// 注册服务
builder.Services.AddAneiangYarpDashboard();

// 注册中间件（UseRouting 之后、MapReverseProxy 之前）
app.UseAneiangYarpDashboard();
```

`AddAneiangYarpDashboard()` 内部完成：

```
1. 配置 RazorPages
2. 注册 DashboardOptions（绑定 Gateway:Dashboard 配置节）
3. 注册 5 个 Controller
4. 注册 DashboardRouteConvention（路由前缀注入）
5. 配置认证（None / ApiKey / DefaultJwt / CustomJwt）
6. 注册 ProxyLogStore（内存日志存储）
7. 可选：AuthorizeRequest 自定义授权
```

`UseAneiangYarpDashboard()` 注册的中间件链：

```
请求进入
  ├── YarpRequestCaptureMiddleware — 捕获经过网关的请求/响应
  ├── MapDashboardEndpoints()     — Razor Pages + API
  └── MapReverseProxy()           — YARP 代理（必须最后）
```

---

## 路由前缀注入

Dashboard 的所有端点都在 `/{prefix}` 下（默认 `apigateway`），通过 `IApplicationModelConvention` 实现：

```csharp
internal sealed class DashboardRouteConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var ctrl in application.Controllers)
        {
            // 只处理 Dashboard 程序集中的 Controller
            if (ctrl.ControllerType.Assembly != typeof(DashboardController).Assembly)
                continue;

            // 跳过已有前缀的 Controller（防重复）
            var existing = ctrl.Selectors
                .FirstOrDefault(s => s.AttributeRouteModel?.Template != null)?
                .AttributeRouteModel?.Template ?? "";
            if (existing.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // 为每个 Action 的路由添加前缀
            foreach (var action in ctrl.Actions)
                foreach (var selector in action.Selectors)
                    if (selector.AttributeRouteModel != null)
                        selector.AttributeRouteModel.Template =
                            _prefix + "/" + selector.AttributeRouteModel.Template;
        }
    }
}
```

**为什么用 Convention 而不是硬编码 `[Route("apigateway/...")]`？**

- 前缀可配置（`RoutePrefix` 选项），Convention 自动适配
- Controller 代码干净，不耦合前缀
- 统一管理，改一处全局生效

---

## 配置管理：快照与回滚

`ConfigManagementController` 提供完整的配置 CRUD 和版本管理。

### 核心操作

| 操作 | 端点 | 关键行为 |
|------|------|---------|
| 集群 CRUD | `PUT/DELETE /apigateway/api/config/clusters/{id}` | 每次写操作前自动保存快照 |
| 集群重命名 | `PUT .../clusters/{id}/rename` | 单次写锁内完成：创建新集群 → 更新引用路由 → 删除旧集群 |
| 路由 CRUD | `PUT/DELETE /apigateway/api/config/routes/{id}` | 自动创建快照 |
| 导入导出 | `GET/POST .../config/export` `.../import` | 标准 YARP 格式，导入时自动校验 |
| 回滚 | `POST .../config/rollback/{versionId}` | 全量替换当前配置 |
| 历史 | `GET .../config/history` | 查看所有变更快照 |

**集群重命名**是最复杂的操作 —— `DynamicYarpConfigService.TryRenameCluster()` 在单次写锁内完成全部步骤，任何一步失败则整体回滚。

---

## 日志采集管道

Dashboard 的日志采集**不依赖任何第三方日志框架**，通过 YARP 的中间件和 Transform 实现：

```
请求进入网关
  ├── YarpRequestCaptureMiddleware（请求前）
  │     └── 记录 Method、Path、Headers、Body
  ├── YARP 反向代理 → 下游服务
  │     └── DownstreamCaptureTransform（响应后）
  │           └── 记录 StatusCode、响应 Headers、Body、耗时
  └── YarpRequestCaptureMiddleware（响应后）
        └── 组装完整日志 → ProxyLogStore（内存环形缓冲）
```

**关键设计**：中间件自动跳过 Dashboard 自身请求（通过路径前缀判断），避免日志污染。

### 生产环境安全控制

日志功能强大，但生产环境要注意安全和性能：

| 配置项 | 效果 |
|--------|------|
| `EnableLogSampling` + `LogSamplingRate: 0.1` | 只记录 10% 的请求 |
| `LogErrorsOnly: true` | 只记录 4xx/5xx |
| `LogMaxBodyLength: 4096` | Body 超 4KB 截断 |
| `LogHeaderBlacklist` | 指定 Header 值替换为 `***REDACTED***` |
| `LogJsonFieldSanitizeList` | JSON 中指定字段自动脱敏 |
| `LogRouteWhitelist` / `LogRouteBlacklist` | 按路由过滤 |

支持 WebSocket 实时推送（`WebSocketLogController`），可以在 Dashboard 页面实时看到日志流。

---

## 四种认证模式

| 模式 | 适合场景 | 配置 |
|------|---------|------|
| `None` | 本地开发 | 零配置 |
| `DefaultJwt` | 个人 / 小团队 | 配一个 `JwtPassword` |
| `CustomJwt` | 企业项目 | 自定义用户名 + 密码 |
| `ApiKey` | API 对接 | Header 传 API Key |

最常用的是 `DefaultJwt`：

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

还可以通过 `AuthorizeRequest` 委托接入自己的认证体系（优先级最高）：

```csharp
builder.Services.AddAneiangYarpDashboard(options =>
{
    options.AuthorizeRequest = async (context) =>
    {
        return context.User.Identity?.IsAuthenticated == true
            && context.User.IsInRole("GatewayAdmin");
    };
});
```

---

## 端点一览

### 页面

| 路径 | 说明 |
|------|------|
| `/apigateway` | Dashboard 首页 |
| `/apigateway/login` | 登录 |
| `/apigateway/clusters` | 集群列表 |
| `/apigateway/routes` | 路由列表 |
| `/apigateway/logs` | 请求日志 |

### API

| 路径 | 说明 |
|------|------|
| `/apigateway/api/config/export` | 导出 YARP 配置 |
| `/apigateway/api/config/import` | 导入配置 |
| `/apigateway/api/config/history` | 变更历史 |
| `/apigateway/api/config/rollback/{id}` | 回滚 |
| `/apigateway/api/config/clusters/{id}` | 集群 CRUD |
| `/apigateway/api/config/routes/{id}` | 路由 CRUD |
| `/apigateway/api/audit-logs` | 审计日志 |

---

## 设计亮点

| 特性 | 实现 | 优势 |
|------|------|------|
| 嵌入式部署 | Razor Class Library | 零构建步骤，一个 DLL |
| 路由前缀可配 | IApplicationModelConvention | 灵活部署，不硬编码 |
| 配置快照 | 写操作前自动保存 | 改坏了一键回滚 |
| 日志采集 | YARP 中间件 + Transform | 不侵入业务代码 |
| 多层脱敏 | Header / Query / JSON | 生产环境安全合规 |
| 四种认证 | None / ApiKey / JWT / 自定义 | 从开发到生产全覆盖 |
| 多语言 | 中英文运行时切换 | 国际化友好 |

**源码地址**：[https://github.com/aneiang/Aneiang.Yarp](https://github.com/aneiang/Aneiang.Yarp)

**在线预览**：http://113.45.65.71:8930/apigateway（admin / demo123）

```bash
dotnet add package Aneiang.Yarp.Dashboard
# Program.cs: builder.Services.AddAneiangYarpDashboard();
#            app.UseAneiangYarpDashboard();
```

---

> **Aneiang.Yarp 源码解析系列**
>
> | [上一篇：网关核心模块](./blog-core.zh-CN.md) | [下一篇：IP 隔离负载均衡](./blog-ip-isolation.zh-CN.md) | [系列目录](./series-index.zh-CN.md) |
>
> 觉得有用？去 [GitHub 点个 Star](https://github.com/aneiang/Aneiang.Yarp) 支持一下。
