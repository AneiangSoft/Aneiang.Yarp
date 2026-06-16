<div align="center">
<img src="Logo.png" alt="LOGO" width="240" style="border-radius: 15px;"/>

**Aneiang.Yarp — 基于 YARP 的全功能 API 网关**

Dashboard · 动态路由 · WAF 防火墙 · 通知告警 · IP 隔离 · 自动注册

[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp)
[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard)
[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Client.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Client)
[![YARP](https://img.shields.io/badge/YARP-2.3.0-blue.svg)](https://github.com/microsoft/reverse-proxy)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-purple.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

[English](README.md) | [中文](README.zh-CN.md)

</div>

---

**Aneiang.Yarp** 是基于 [微软 YARP](https://microsoft.github.io/reverse-proxy/) 2.3.0 构建的生产级 API 网关增强方案。它在 YARP 基础上补齐了所有生产环境所需的配套设施：可视化管理面板、WAF 防火墙、通知告警、健康监控、熔断器面板、客户端自动注册、IP 隔离负载均衡 — 全部通过三个 NuGet 包交付。

> **文档地址**：https://yarp.aneiang.com

> **在线演示**：https://yarp-test.aneiang.com/aneiang &nbsp;·&nbsp; `admin` / `demo123`

> **欢迎关注我的公众号：** ![关注公众号](https://img.shields.io/badge/关注-递归不爆炸-green)
>
![扫码关注公众号](docs/wechat_qrcode.jpg)
---

## 包结构

| 包 | 用途 | 依赖 YARP |
|:----|:------|:---:|
| **Aneiang.Yarp** | 网关核心：动态路由引擎（`IDynamicYarpConfigService`）、配置持久化、API 鉴权体系、IP 隔离负载均衡（`IpBasedLoadBalancingPolicy`）、存储层抽象（8 个独立仓储接口）、配置变更事件模型、gRPC 注册协议 | ✅ |
| **Aneiang.Yarp.Dashboard** | Web 管理面板：全量 CRUD、WAF 防火墙（`WafMiddleware`）、通知告警（钉钉/通用 Webhook）、健康检查监控、熔断器管理（`CircuitBreakerMiddleware`）、代理日志（`YarpRequestCaptureMiddleware`）、审计日志、配置快照与回滚、策略引擎（`GatewayPolicyService`）、限流/重试中间件、SignalR 实时流量推送、SQLite 持久化（8 个 `Sqlite*Repository`） | 通过核心库 |
| **Aneiang.Yarp.Client** | 客户端 SDK：`GatewayRegistrationHostedService` 启动自动注册 + 关闭自动注销、Kestrel 自动配置，一行代码接入。零 YARP 依赖，仅需 `Microsoft.AspNetCore.App` | ❌ |

```
Aneiang.Yarp.Dashboard
  └── Aneiang.Yarp
        └── Aneiang.Yarp.Client

客户端微服务 → 仅引用 Aneiang.Yarp.Client（不引入 YARP SDK）
网关项目     → 引用 Aneiang.Yarp + Aneiang.Yarp.Dashboard
```

### 技术栈

| 层级 | 技术 | 说明 |
|:-----|:-----|:-----|
| 反向代理引擎 | Microsoft YARP 2.3.0 | 高性能 HTTP 反向代理 |
| 运行时 | .NET 8.0 / 9.0 | 跨平台 |
| 存储 | SQLite（支持 SQLCipher AES-256 加密） | 嵌入式，零外部服务依赖 |
| 通信 | REST API + gRPC + SignalR | HTTP 管理、gRPC 注册、WebSocket 实时推送 |
| 认证 | JWT (HMAC-SHA256) + API Key | 内置登录页面，五种模式可切换 |
| Dashboard 技术 | Razor Pages + 原生 JavaScript | 无 Node.js 依赖，响应式布局 |

---

## Dashboard

两行代码启用完整的管理面板：

```csharp
builder.Services.AddAneiangYarpDashboard();
// ...
app.UseAneiangYarpDashboard();
```

<p align="center">
  <img src="docs/overview.png" alt="Dashboard 概览" width="800"/>
</p>

### 全部 15 个管理页面

| 分组 | 页面 | 详细功能 |
|:------|:-----|:-----|
| **概览** | 项目概览 | 活跃路由/集群计数、流量 QPS 概览、集群健康状态快速预览、最近变更时间线 |
| **网关** | 集群管理 | 创建/编辑/删除集群（含多目标节点），配置主动/被动健康检查参数（检测端点、超时、间隔、可用目标策略），`HttpRequest` 与 `HttpClient` 配置 |
| | 路由管理 | 管理路由规则（含 Transform 转换器、Metadata 元数据、`Waf:Enabled` 按路由开关），拖拽排序优先级，`CorsPolicy` 跨域配置 |
| **监控** | 统计面板 | 请求量趋势、P50/P90/P99 延迟分位数、HTTP 状态码分布饼图、Top 路由排行 |
| | 请求日志 | 实时代理日志流（WebSocket 推送），按路由/状态码/TraceID 组合过滤，敏感信息脱敏展示，支持采样与仅记录错误两种模式 |
| | 熔断器 | 每集群/节点实时熔断状态：Closed（绿色）/Open（红色）/HalfOpen（黄色），连续失败次数 vs 阈值、恢复倒计时、熔断触发时间。一键重置所有熔断器 |
| | 通知中心 | 管理 Webhook 渠道（钉钉机器人/通用 HTTP），配置事件规则（按事件类型独立开关+冷却时间），查看通知发送历史与状态 |
| | 健康检查 | 集群级健康概览表格，按目标节点钻取展示实时健康状态、最近检查时间与结果 |
| **安全** | 安全事件 | WAF 攻击日志实时看板（自动刷新），攻击类型分布统计、Top 攻击来源 IP、每条记录包含命中规则与匹配内容 |
| | WAF 防火墙 | IP 黑白名单编辑（精确 IP/CIDR 网段/通配符），SQL 注入、XSS、路径遍历检测开关，请求大小限制参数，安全响应头配置 |
| | 策略管理 | 流量策略创建/编辑/启用/禁用/排序，支持重试策略、超时策略、限流策略、请求转换策略等类型 |
| | 插件管理 | 查看所有已注册的 `IGatewayPlugin` 插件（WAF 等），查看插件元数据与加载顺序 |
| **系统** | 配置历史 | 每次变更前自动版本快照列表，可查看任意快照完整内容，一键回滚到指定历史版本 |
| | 审计日志 | 完整操作审计轨迹：操作类型（创建/更新/删除/导入/回滚）、操作对象、操作人、变更前后 JSON Diff、精确时间戳 |
| | 系统设置 | 认证模式切换、JWT 密钥配置、日志配置（采样率/脱敏列表/Body 长度限制）、一键下载 SQLite 数据库文件、语言切换 |

---

## 核心功能

### 动态路由

YARP 原生通过 `appsettings.json` 或 `InMemoryConfigProvider` 管理路由，修改后需要手动更新内存状态。Aneiang.Yarp 在此基础上构建了完整的运行时路由管理体系：

**双层配置源合并机制**：
- **静态配置层**：`appsettings.json` 中 `ReverseProxy` 节点定义的基础路由，适合网关自身依赖的路由
- **动态配置层**：通过 Dashboard UI 或 REST API 注册的路由，存储在 `DynamicYarpConfigService` 中
- **合并策略**：启动时 `DynamicYarpConfigService.BuildMergedConfig()` 将静态与动态配置合并去重，注入 YARP 的 `InMemoryConfigProvider`，YARP 原生热更新机制确保实时生效

**自动持久化与恢复**：
- 所有动态路由/集群变更即时写入 `gateway-dynamic.json`（应用根目录）
- 网关重启时自动从该文件恢复全部动态配置，无需人工干预
- 配置文件格式与 YARP 标准配置兼容

**元数据追踪**：每个动态路由和集群自动记录 `CreatedAt`（创建时间）、`CreatedBy`（操作来源：Dashboard / API / Client / Import）、`Source`（来源标识）

**线程安全保障**：所有对 `_routes` 和 `_clusters` 字典的读写通过 `SemaphoreSlim(1,1)` 保护。相比 `lock`，`SemaphoreSlim` 原生支持 async/await，不阻塞线程池线程，高并发下性能更优

提供完整的网关管理 REST API，支持路由和集群的 CRUD 操作、动态配置查询、健康检查等。

### WAF 防火墙

内建于网关中间件管道的生产级 Web 应用防火墙，`WafMiddleware` 在请求到达反向代理之前执行安全检查，零外部服务依赖。所有检测规则关闭时，中间件直接透传，零性能损耗。

| 防护能力 | 实现细节 |
|:-----------|:--------|
| **IP 黑白名单** | 精确 IP、CIDR 网段（`192.168.1.0/24`）、通配符匹配。白名单优先：命中白名单直接放行，命中黑名单返回 403。IP 匹配使用预编译正则缓存，`RegexOptions.Compiled` |
| **SQL 注入检测** | 2 组预编译正则：关键字组（`SELECT`、`UNION`、`DROP`、`INSERT` 等）+ 注入值组。每条正则含 5ms ReDoS 超时，防恶意构造输入耗尽 CPU |
| **XSS 检测** | 检测 `<script>` 标签、`javascript:` 伪协议、事件处理器注入（`onerror`、`onload`、`onclick`）、`eval` 表达式 |
| **路径遍历检测** | 检测 `../` 及 URL 编码变体（`%2e%2e/`、`%2e%2e%2f`）、`..\`、双重编码攻击（`%252e%252e%252f`） |
| **请求限制** | 请求体上限（默认 10MB，超限返回 413）、请求头数量上限（100）、单头大小上限（8KB）、URI 长度限制（4096） |
| **安全响应头** | 自动注入：`X-Content-Type-Options: nosniff`、`X-Frame-Options: DENY`、`X-XSS-Protection: 1; mode=block`、`Referrer-Policy: strict-origin-when-cross-origin`、可配置 CSP |
| **按路由控制** | YARP 路由 Metadata 中 `Waf:Enabled` 键值精确控制。可对敏感路径（`/api/admin/*`）独立开启，静态资源路径关闭以降低开销 |

**安全事件**：每次拦截生成 `WafSecurityEvent` 记录（来源 IP、攻击类型、命中规则、路径、时间戳），持久化存储并在安全事件页面实时自动刷新，支持按攻击类型和来源 IP 统计排行。

### 通知告警

`NotificationService` + `ConfigChangeEventDispatcher`（BackgroundService）构成多渠道告警体系：

**支持的渠道**：
- **钉钉机器人**：通过 Webhook 推送 Markdown 格式消息到群聊，可配 `@所有人`
- **通用 HTTP Webhook**：发送 JSON 告警体到任意端点，支持自定义 Headers，适配 AlertManager 等内部告警平台
- 每个渠道独立配置超时（毫秒级）和重试次数

**事件类型与触发条件**：

| 事件类型 | 触发条件 | 典型场景 |
|:---------|:---------|:---------|
| 熔断器打开 | 连续失败超过 `FailureThreshold` | 下游服务故障 |
| 重试耗尽 | 所有重试均失败 | 网络波动 |
| WAF 拦截 | 任意 WAF 规则命中 | 攻击检测 |
| 代理错误 | 反向代理返回 5xx | 目标服务异常 |
| 限流触发 | 请求超过阈值被拒 | 流量突增 |
| 配置变更 | 路由/集群 CRUD | 运维记录 |

**冷却机制**：每个事件规则独立配置冷却时间（秒级），同类型事件在窗口内不重复发送，防告警风暴。**队列分发**：`ConfigChangeEventDispatcher` 以 200ms 间隔从 `ConcurrentQueue<PendingNotification>` 拉取异步发送，不阻塞主管道。结果记入 `NotificationHistory` 表供回溯。

### 健康检查监控

- **主动健康检查**：按配置的 `HealthCheckEndpoint`（如 `/health`）、`Interval`（检查间隔）、`Timeout`（超时）定期向目标发送 HTTP 请求。支持自定义方法、请求头、预期状态码范围
- **被动健康检查**：基于实际请求的失败率判定。YARP 原生支持被动策略（如连续 N 次失败将目标标记为 Unhealthy），Aneiang.Yarp 将其配置暴露到 Dashboard UI 并持久化
- **健康评分与钻取**：Dashboard 展示各集群下每个目标节点的实时健康状态和评分，Unhealthy 节点高亮标红，支持点击钻取查看详细失败原因和时间线
- **`DefaultHealthCheckService`**：启动时自动检查所有集群。若某集群未配置主动检查但存在被动检查参数，则自动创建默认主动策略，防止遗漏

### 熔断器面板

`CircuitBreakerMiddleware` 采集 YARP 熔断器数据，通过独立页面展示：

- **三种状态可视化**：**Closed**（绿色，正常）、**Open**（红色，请求快速失败，等 `RecoveryTimeout` 到期）、**HalfOpen**（黄色，试探少量请求，成功回 Closed，失败回 Open）
- **关键指标**：每节点连续失败次数 vs 熔断阈值、恢复倒计时、熔断触发时间
- **一键重置**：管理员确认服务恢复后可强制将所有熔断器重置为 Closed

### 网关策略引擎

`GatewayPolicyService` 管理网关级流量控制策略，运行时创建、编辑、排序、启用/禁用：

| 策略类型 | 说明 |
|:---------|:-----|
| 重试策略 | 失败重试次数、重试间隔、可重试的 HTTP 状态码范围 |
| 超时策略 | 连接超时、响应超时时间设置 |
| 限流策略 | 每秒/每分钟最大请求数限制 |
| 请求转换 | 请求头增删改、路径重写等规则 |

策略通过 YARP Metadata 与路由关联，一策略可应用于多路由。持久化到 SQLite（`IPolicyRepository`），修改即时生效。

### 网关中间件管道

Aneiang.Yarp 在 YARP 管道中按以下顺序插入中间件：

```
请求进入
  → RateLimitMiddleware          (限流检查)
  → WafMiddleware                (安全检测)
  → RequestRetryMiddleware       (重试控制)
  → CircuitBreakerMiddleware     (熔断器控制)
  → BuiltinTransformMiddleware   (内置请求转换)
  → YarpRequestCaptureMiddleware (请求/响应数据捕获)
  → YARP 反向代理转发
  → 响应返回
```

### 网关插件系统

`GatewayPluginManager` 提供可扩展中间件插件体系：

- **接口 `IGatewayPlugin`**：定义插件元数据（`Name`、`Description`、`Version`、`Order` 优先级）+ `Enabled` 开关
- **自动发现**：通过 DI 收集所有 `IGatewayPlugin` 注册，按 `Order` 排序执行
- **内置插件**：WAF 等核心安全模块以插件形式注册
- **插件管理页面**：Dashboard 可查看所有已加载插件的元数据、加载顺序与运行状态

### 客户端自动注册

微服务启动自动注册、关闭自动注销 — **真正意义上一行代码**：

```csharp
builder.Services.AddAneiangYarpClient();
```

**`GatewayRegistrationHostedService` 执行流程**：
1. 应用启动（`IHostedService.StartAsync`）→ `GatewayAutoRegistrationClient.RegisterAsync()`
2. 自动解析 Kestrel 绑定地址和端口（`localhost` 自动转换为局域网 IP）
3. 构建注册请求（路由名、集群名、匹配路径、目标地址、负载均衡策略、IP 隔离开关）
4. 向网关 REST API 或 gRPC 端点发送注册
5. 应用关闭（`StopAsync`）→ `UnregisterAsync()` 发送注销

**智能默认值** — 只需 `GatewayUrl`，其余全自动推断：

| 配置项 | 默认值 | 说明 |
|:-------|:--------|:------------|
| `RouteName` | 入口程序集名称 | `Assembly.GetEntryAssembly().GetName().Name` |
| `ClusterName` | 同 RouteName | |
| `MatchPath` | `/{**catch-all}` | 匹配所有路径 |
| `DestinationAddress` | Kestrel 绑定地址 | 自动检测，`localhost` → 局域网 IP |
| `Order` | `50` | 路由优先级（越小越优先） |
| `LoadBalancingPolicy` | `PowerOfTwoChoices` | YARP 默认策略 |
| `UseIpIsolation` | `false` | 是否启用 IP 隔离 |

**三种通信与认证模式**：
- **REST API**：向网关管理端点发送注册请求，支持 Bearer Token / Basic Auth / API Key
- **gRPC**：通过 `GatewayRegistry.proto` 协议，端口 = HTTP 端口 + 1，Kestrel 自动配置 HTTP/2 h2c。`GatewayRegistryGrpcService` + `GrpcAuthInterceptor` 认证拦截
- **协议选择**：客户端自动检测网关是否支持 gRPC，优先 gRPC，降级 REST

**指数退避重试**：启动注册失败时 — 2s → 4s → 8s → 16s → 30s，最多 5 次。网关暂时不可用时下游服务仍能正常启动，恢复后自动注册。

> `Aneiang.Yarp.Client` **极简依赖** — 仅 `Microsoft.AspNetCore.App`，不会将 YARP 及其约 10+ 个包间接拉入微服务。

### IP 隔离负载均衡

专为**多人协作开发调试**设计的特色功能。多开发者在各自机器本地启动同一微服务并开启 `UseIpIsolation: true` 注册。网关按客户端请求来源 IP 自动将请求路由到对应开发者机器的服务实例，**前端代码完全不用修改**。

```
开发者 A (192.168.1.10) → POST /api/user → 网关 → 192.168.1.10:5001  (A 的本机实例)
开发者 B (192.168.1.20) → POST /api/user → 网关 → 192.168.1.20:5001  (B 的本机实例)
未匹配到的用户         → POST /api/user → 网关 → 第一个可用实例（降级负载均衡）
```

**实现原理**：
- 自定义 `IpBasedLoadBalancingPolicy` 注册为 YARP 负载均衡策略
- 获取客户端真实 IP：优先 `X-Forwarded-For` 请求头，其次 `HttpContext.Connection.RemoteIpAddress`
- 在集群所有 Destination 的 Metadata 中匹配 IP
- 匹配成功返回对应节点，未匹配降级为默认负载均衡策略
- 路由删除时自动清理 IP 隔离关联

### 配置管理

**导入/导出**：
- **导出**：一键将当前网关完整 YARP 配置（路由 + 集群 + 元数据）导出为标准 JSON 下载
- **导入**：上传 JSON 配置文件 → 格式校验 → 「导入前」快照 → 持久化，三段式流程。校验失败回滚，保证不产生不完整配置

**版本快照与回滚**：
- 每次配置变更（创建/编辑/删除/导入）前，系统自动将当前完整配置保存为快照
- 快照含全局唯一 ID、时间戳、配置类型、完整 JSON
- 配置历史页面展示所有快照，查看内容 + 一键回滚。回滚操作本身也会触发新快照

**审计日志**：
- 所有 Dashboard 和 API 操作全程留痕：操作类型（Create/Update/Delete/Import/Rollback）、操作对象、操作人、变更前后 JSON Diff、毫秒时间戳
- `ConfigChangeAudit` 模型记录，`IAuditLogRepository` 持久化，Dashboard 审计日志页面支持按类型和对象筛选

**SQLCipher 加密**：连接字符串追加 `Password=xxx` 启用 AES-256 全数据库加密。加密对上层完全透明，不加密码则标准 SQLite。**数据库下载**：设置页面一键下载 SQLite 文件方便本地分析工具查看。

### 请求日志

`YarpRequestCaptureMiddleware` 插入 YARP 管道，转发前捕获请求端数据、响应返回后捕获响应端数据：

**全量信息**：HTTP 方法、完整 URL（含查询参数）、请求/响应头、请求/响应 Body（限制 `LogMaxBodyLength`）、响应耗时（毫秒）、TraceId。**实时推送**：`WebSocketLogController` 提供 WebSocket 端点，Dashboard 日志页面实时接收无需手动刷新。

**脱敏**：
- `LogHeaderBlacklist`：需脱敏 Header 名列表（`Authorization`、`Cookie`、`Set-Cookie`、`X-Api-Key`），值显示 `***`
- `LogJsonFieldSanitizeList`：需脱敏 JSON 字段名列表（`password`、`token`、`secret`、`apikey`、`creditCard`），递归扫描替换

**采样与过滤**：`EnableLogSampling` + `LogSamplingRate`（0.0~1.0，生产建议 0.1）；`LogErrorsOnly` 仅记录 4xx/5xx。Dashboard 支持按路由名、状态码范围、TraceID 组合过滤。

**结构化存储**：`YarpEventFormatter` → `StructuredLogService` → `IProxyLogRepository`（SQLite），`PipelineLogWriter` 批量写入优化减少事务开销。

### 认证

Dashboard 和网关管理 API 支持五种认证模式，通过 `AuthMode` 配置切换：

| 模式 | 说明 |
|:-----|:------------|
| `None` | 无认证，直接访问（默认，仅适用于开发环境） |
| `DefaultJwt` | 内置 JWT 认证：用户名固定 `admin`，密码通过 `JwtPassword` 设定。`JwtSecret` 不配则自动生成（保存到 `.jwt-secret` 文件，重启不丢失）。Dashboard 自动渲染登录页面，Token 有效期内免复登 |
| `CustomJwt` | 自定义 `JwtUsername`/`JwtPassword`/`JwtSecret`，其余行为同 DefaultJwt |
| `ApiKey` | 通过请求头 `X-Api-Key` 携带密钥，适用于脚本/CI/CD 非交互式场景 |
| `AuthorizeRequest` 委托 | 注入自定义 `Func<HttpContext, Task<bool>>` 委托，接入企业现有认证（AD/LDAP/OAuth2），优先级最高 |

**智能凭据推断**：网关同时配置 Dashboard JWT 和管理 API 认证（`AddGatewayApiAuth()`）时，API 认证过滤器自动读取 Dashboard JWT 配置验证客户端 Bearer Token，无需重复配置。

**认证优先级**：`AuthorizeRequest` > `ApiKey` > `JWT` > `None`

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
app.UseAneiangYarpDashboard();  // 内部已包含 MapReverseProxy
app.MapControllers();
app.Run();
```

Dashboard 地址：`/apigateway`。

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

builder.Services.AddAneiangYarpClient();   // ← 启动时自动注册
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

微服务启动时自动注册到网关，关闭时自动注销。无需额外代码。

---

## 配置参考

所有配置位于 `Gateway:*` 下 — **全部有默认值，零配置即可启动**。

```json
{
  "Gateway": {
    "Dashboard": {
      "RoutePrefix": "apigateway",
      "Locale": "zh-CN",
      "EnableProxyLogging": true,

      "AuthMode": "DefaultJwt",
      "JwtPassword": "your-strong-password",
      "JwtSecret": "...",

      "EnableLogSampling": false,
      "LogSamplingRate": 1.0,
      "LogErrorsOnly": false,
      "LogMaxBodyLength": 8192,
      "LogHeaderBlacklist": ["Authorization", "Cookie", "Set-Cookie"],
      "LogJsonFieldSanitizeList": ["password", "token", "secret", "apikey", "api-key"]
    },
    "Storage": {
      "Sqlite": {
        "ConnectionString": "Data Source=gateway-store.db;Password=your-password"
      }
    }
  }
}
```

### Dashboard 选项

| 配置项 | 默认值 | 说明 |
|:-------|:--------|:------------|
| `RoutePrefix` | `"apigateway"` | Dashboard URL 前缀 |
| `EnableProxyLogging` | `true` | 请求日志总开关 |
| `Locale` | `"zh-CN"` | 默认语言（`zh-CN` / `en-US`），运行时可切换 |
| `AuthMode` | `None` | `None` / `ApiKey` / `CustomJwt` / `DefaultJwt` |
| `JwtPassword` | — | JWT 登录密码 |
| `JwtUsername` | — | 用户名（仅 CustomJwt；DefaultJwt 固定 `admin`） |
| `JwtSecret` | 自动生成 | JWT 签名密钥，不配则重启后重新生成 |
| `ApiKey` | — | ApiKey 模式的 Key 值 |
| `EnableLogSampling` | `false` | 启用按比例日志采样 |
| `LogSamplingRate` | `1.0` | 采样率 0.0–1.0 |
| `LogErrorsOnly` | `false` | 仅记录 4xx/5xx 响应 |
| `LogMaxBodyLength` | `8192` | 记录 Body 最大长度（字节） |
| `LogHeaderBlacklist` | — | 日志中脱敏的 Header 列表 |
| `LogJsonFieldSanitizeList` | — | 日志中脱敏的 JSON 字段列表 |

### Storage 选项

| 配置项 | 默认值 | 说明 |
|:-------|:--------|:------------|
| `ConnectionString` | `Data Source=gateway-store.db` | SQLite 连接字符串。添加 `Password=xxx` 启用 SQLCipher AES-256 加密。 |

### WAF 选项

| 配置项 | 默认值 | 说明 |
|:-------|:--------|:------------|
| `Enabled` | `false` | WAF 总开关 |
| `IpWhitelist` | — | IP 白名单（每行一个 IP 或 CIDR） |
| `IpBlacklist` | — | IP 黑名单（每行一个 IP 或 CIDR） |
| `MaxRequestBodySize` | `10485760` | 请求体最大字节数 |
| `MaxHeaderCount` | `100` | 最大请求头数量 |
| `MaxHeaderSize` | `8192` | 单个请求头最大字节数 |
| `EnableSqlInjectionDetection` | `true` | 检测 SQL 注入模式 |
| `EnableXssDetection` | `true` | 检测 XSS 模式 |
| `EnablePathTraversalDetection` | `true` | 检测路径遍历攻击 |
| `EnableIpCheck` | `true` | 启用 IP 黑白名单检查 |
| `EnableRequestSizeValidation` | `true` | 强制请求体大小限制 |

---

## 高级用法

<details>
<summary><b>自定义授权 — 接入你自己的认证体系</b></summary>

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

优先级：`AuthorizeRequest` > `ApiKey` > `JWT` > `None`

</details>

<details>
<summary><b>网关 API 鉴权 — 智能凭据推断</b></summary>

网关配了 Dashboard JWT 认证后，网关管理 API 自动读取：

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
app.UseAneiangYarpDashboard();  // ← 在 UseRouting 之后（内部已包含 MapReverseProxy）
app.MapControllers();
```

中间件负责捕获 YARP 代理的请求/响应数据，自动跳过 Dashboard 自身请求。

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
    },
    "Storage": {
      "Sqlite": {
        "ConnectionString": "Data Source=gateway-store.db;Password=your-production-password"
      }
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

Dashboard：`/apigateway` · 登录：`admin` / `demo123`

---

## NuGet

| 包 | 说明 | NuGet |
|:--------|:------------|:-----:|
| **Aneiang.Yarp** | 网关核心：动态路由、IP 隔离、API 鉴权 | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp) |
| **Aneiang.Yarp.Client** | 客户端自动注册（轻量，无 YARP 依赖） | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Client.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Client) |
| **Aneiang.Yarp.Dashboard** | Web 管理面板：全功能可视化管理 | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard) |

**.NET 8.0 / 9.0** &nbsp;·&nbsp; **YARP 2.3.0**

---

## 许可证

[MIT](LICENSE)

---

<div align="center">

觉得有用？[⭐ Star 一下](https://github.com/aneiang/Aneiang.Yarp) 支持项目发展

</div>
