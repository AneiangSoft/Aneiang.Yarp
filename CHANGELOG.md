# 更新日志


## [2.3.0.25] - 2026-06-30

> 代码质量提升：消除同步等待死锁风险、重复代码抽取、类职责拆分

### 🛡️ 代码质量

- **消除 5 处 `GetAwaiter().GetResult()` 死锁风险**：`DynamicYarpConfigService` 启动加载链路全异步化（`LoadConfigFromRepositoryAsync` / `LoadDynamicConfigAsync` / `MarkStaticConfigAsync`），`WafSettingsPersistenceService` `Load()`/`Save()` 改为 `Task.Run` 安全包装
- **`DynamicYarpConfigService` 职责拆分**：提取 ~200 行纯函数到 `DynamicYarpConfigHelpers`（7 个静态方法：序列化/补丁/健康检查/元数据合并/X-Forwarded 标准化），主文件行数显著减少
- **统一 ClientIpResolver**：消除 5 处重复 IP 解析代码（WafMiddleware / RateLimitMiddleware / ConfigManagementController / IpBasedLoadBalancingPolicy / RateLimitConfigProvider），合并到 `Aneiang.Yarp.Infrastructure.ClientIpResolver`
- **DI 注入修复**：`DashboardMvcOptionsSetup` 不再手动 `new DashboardAuthorizationService`，改为通过 DI 注入 `IDashboardAuthorizationService`
- **静默异常加日志**：`DashboardRouteQueryService` 添加 `ILogger` 依赖，transform 提取失败时输出 `LogWarning`
- **SQLite 表名校验**：`BackfillInBatchesAsync` 增加白名单校验（6 张合法表），防止内部代码注入
- **移除废弃 API**：`AddAneiangYarpDeployment(IServiceCollection, IConfiguration)` 已无调用方，直接删除
- **GC.Collect 注释补充**：`TriggerBackgroundGc()` 添加 Gen0 + Optimized 调用的性能理由注释
- **移除未使用方法**：删除 `SanitizeClusters`（无调用方）

### 🐛 修复

- **Dashboard 路由/集群重启后消失**：修复 `MarkStaticConfigAsync` 启动时误删所有非 appsettings.json 来源的路由和集群（Source != "config"），并持久化删除到 SQLite。改为仅当 `Source == "config"` 且 ID 不在当前静态配置中时才清除（`DynamicYarpConfigService` 第 217 / 250 行）

### 🗂️ 新增文件

```
src/Aneiang.Yarp/
├── Infrastructure/
│   └── ClientIpResolver.cs                         # 统一客户端 IP 解析
└── Services/
    └── DynamicYarpConfigHelpers.cs                  # 静态工具方法（序列化/元数据合并/健康检查等）
```


## [2.3.0.24] - 2026-06-28

> Dashboard 功能完善 + 全站国际化适配 + 代码精简

### 🚀 新功能

- **Dashboard 支持完整 YARP 配置**：路由的 Host/Header/Query/CORS/Auth 匹配、集群的会话保持/健康检查/HTTP 配置等高级属性全部可用
- **请求先行日志**：请求一进来就显示在日志页，不用等响应返回
- **自动 CORS 和授权**：无需手动配置中间件，开箱即用

### 🐛 修复

- 配置修改后刷新页面不再丢失高级属性
- 差异对比面板不再显示 null，增删条目显示实际内容
- 导入配置后自动生成导入后快照
- 统计/历史/通知/部署等页面切换英文时不再出现中文

### 🗑️ 清理

- 移除热重载模块（`ConfigurationFileWatcher`）
- 精简日志系统，只保留请求/响应日志
- 移除运行模式页面的手动操作按钮

### 🎨 改进

- 16 个页面统一初始化逻辑，减少重复代码
- 日志页样式优化，自适应屏幕高度


## [2.3.0.22] - 2026-06-26

> 原生 YARP 配置完整保留 + 端口重复绑定修复 + 全局加载指示器 + JSON 宽松解析

### 🚀 新增

- **原生 YARP 配置完整保留**：通过 Dashboard 编辑/保存路由和集群时，所有高级属性（SessionAffinity、HttpClient、HttpRequest、完整 Match 条件、Auth/Cors/Timeout 策略等）完整保留，不再丢失
- **全局加载指示器**：API 请求时显示顶部进度条，加载失败提供重试按钮，离线状态自动检测
- **JSON 宽松解析**：配置编辑器和导入功能支持 JSON 注释（`//`、`/* */`）和尾逗号，兼容标准 YARP 配置文件风格
- **路由匹配大小写敏感**：Header / QueryParameter 匹配支持 `IsCaseSensitive` 选项

### 🐛 修复

- **端口重复绑定**：配置 `Kestrel:Endpoints` 时不再报 "address already in use" 错误
- **集群 HTTP Request 配置丢失**：集群详情中 HttpRequest 配置（ActivityTimeout、Version 等）现在正确显示
- **配置导入大小写不兼容**：导入配置时 `Routes`/`routes`、`Clusters`/`clusters` 等键名均可识别

### 🎨 优化

- 审计日志页添加加载动画
- 集群加载失败显示可重试错误提示

### 📝 文档

- README 添加公众号二维码
- 新增完整 YARP 配置参考示例 `docs/yarp_all.json`


## [2.3.0.21] - 2026-06-24

> 灵活端口与启动模式 + 健康检查 + 配置热更新 + 2FA 两步验证 + 企业化 UI 重构 + 前端性能优化 + 代码清理

### 🚀 新增功能

#### 灵活端口与启动模式
- **多端口监听**：通过 `Kestrel:Endpoints` 配置多个端点，每个端点绑定不同端口和 IP
- **5 种启动模式**：`Auto` / `AllInOne` / `Split` / `ProxyOnly` / `DashboardOnly`
- **端点角色路由**：`EndpointRouterMiddleware` 根据 `LocalPort` + 角色决定请求归属
- **角色枚举**：`Proxy` / `Dashboard` / `Admin` / `Health` / `All`
- **安全防护**：`RequireLoopbackForDashboard` 默认 true，公网绑定 Dashboard 启动失败
- **命令行快捷参数**：`--deployment split --proxy-url ... --dashboard-url ...`
- **完全向后兼容**：现有 `Urls` 配置自动生效，行为不变

#### 健康检查
- **3 个端点**：`/health`（综合）、`/ready`（就绪）、`/live`（存活）
- **依赖检查**：可配置是否检查数据库连接、YARP 配置加载
- **鉴权选项**：可选 IP 白名单、可选 Token 鉴权（`X-Health-Token` 头或 `?token=` 查询）
- **K8s 友好**：返回 JSON 包含 `status` / `checks` / `uptime` / `version` / `endpoints`

#### 配置热更新
- **FileSystemWatcher 主监听**：监听 `appsettings.json` 和 `appsettings.{Env}.json`
- **30s 兜底轮询**：防止编辑器 temp+rename 模式下事件丢失
- **500ms 防抖**：避免编辑器多次保存触发连续重载
- **失败自动回滚**：重载异常时恢复上次快照
- **配置快照**：保留最近 5 个版本，支持手动回滚（`.config-snapshots/` 目录）
- **端点变更检测**：Kestrel:Endpoints 变更时发出告警（不自动重启）

#### 2FA 两步验证（TOTP）
- **TOTP 验证器**：`TotpHelper` 生成 Base32 密钥 + `otpauth://` URI，支持 Google Authenticator 等验证器应用
- **登录页 2FA 适配**：登录时检测 2FA 启用状态，返回 202 时展示验证码输入框，按钮文字切换"登录"→"验证"
- **设置页 2FA 管理**：配置 2FA（生成密钥+二维码）、验证绑定、关闭 2FA、实时状态刷新
- **运行时状态持久化**：2FA 启用状态保存到 `twofactor-state.json`，重启不丢失
- **i18n 支持**：新增 `login.verify`、`login.verifying`、`config.twofactor*` 等中英文键

#### 系统健康监控
- **DashboardInfoResponse 扩展**：新增 CPU 使用率、内存工作集、总内存、GC 次数、线程数字段
- **DashboardInfoQueryService 增强**：实时计算 CPU%（处理器时间/运行时间）、GC 收集次数、线程数

### 🎨 UI/UX 企业化重构

#### 登录页重新设计
- **毛玻璃卡片**：`backdrop-filter: blur(20px)` + 半透明白色背景
- **品牌色渐变面板**：靛蓝→紫→蓝渐变，玻璃态图标，装饰圆圈
- **气泡动画背景**：24 个小气泡从底部上浮，6 色随机，左右摇摆+缩放
- **渐变登录按钮**：阴影 + hover 上浮效果
- **底部版本信息**：`v2.3.0 · MIT License · © 2026 Aneiang.Yarp`

#### 侧边栏菜单重构
- **分组折叠**：5 个分组（仪表盘/网关管理/监控运维/安全策略/系统管理），点击标题折叠/展开
- **当前页自动展开**：进入页面时自动展开所在分组，其余折叠
- **品牌区**：三色渐变图标 + 光晕 + 副标题
- **用户卡片**：渐变头像 + 右下角脉冲在线指示
- **底部操作**：图标按钮组（语言切换、退出登录）
- **字体调大**：body 14→15px，菜单项 13→14px，分组标题 10.5→13px

### ⚡ 性能优化

#### 前端资源优化
- **字体 TTF→WOFF2**：5 个 Inter 字体 1,590KB → 553KB（节省 65%）
- **缓存启用**：移除 `Cache-Control: no-store`，已有 `?v=版本号` 机制可安全缓存
- **压缩优化**：Brotli/Gzip 压缩级别 Fastest→Optimal，新增字体 MIME 类型
- **Monaco 精简**：删除 8 个不使用的 NLS 语言包（-1,747KB）+ 74 个 basic-languages 目录（-450KB）
- **按页面拆分模块**：17 个页面模块从 Layout 移至各页面 `@section Scripts`，每页减少 ~500-700KB
- **编辑器脚本按需加载**：8 个编辑器脚本从 Layout 移除，仅 Settings/Clusters/Routes 页加载
- **13 个页面清理重复核心脚本**：删除 ~97 行重复 `<script src>` 引用

### 🐛 修复

- **2FA 验证码错误返回码**：401→400，避免前端 API 客户端将其当作认证失效跳转登录页
- **2FA API 响应解包**：`Settings.cshtml` 中 `resp.data.secret` → `resp.secret`（DashboardApi 已自动解包）
- **dashboard-plugins.js 语法错误**：删除 `resetAll` 函数后的孤立重复代码块
- **DashboardApp 未定义**：在 `dashboard-core.js` 中定义 `DashboardApp` 对象（modules 注册表 + registerModule + navigateTo）
- **首页数据绑定**：`Overview.cshtml` 添加 `dashboard-home.js` + 直接 API 兜底渲染
- **loadSystemHealth API 调用**：`DashboardApi.getInfo()`（不存在）→ `DashboardApi.endpoints.getInfo()`
- **ServiceTabs 初始化时序**：DOMContentLoaded 已触发时立即执行而非注册监听

### 🧹 代码清理

- **前端 JS**：删除 48 条 `console.log`、277 条分隔线注释、59 条冗余注释（共 384 条）
- **后端 C#**：删除 42 条分隔线注释（`// ───` / `// ====`），保留 XML 文档注释
- **push-nuget.ps1 更新**：新增 2 个 Storage 项目、依赖顺序打包、`-SkipRestore` 参数

### 📁 新增文件

```
src/Aneiang.Yarp.Dashboard/
├── Infrastructure/
│   ├── Deployment/
│   │   ├── DeploymentOptions.cs        # 配置选项 + 5 种枚举
│   │   ├── EndpointRoleResolver.cs     # 端口→角色映射
│   │   ├── DeploymentConfigValidator.cs # 启动验证
│   │   ├── ConfigSnapshot.cs           # 快照模型 + 文件存储
│   │   └── DeploymentCli.cs            # 命令行解析
│   ├── Middleware/
│   │   ├── EndpointRouterMiddleware.cs # 端口路由分发
│   │   └── HealthCheckMiddleware.cs    # 健康检查
│   ├── HostedServices/
│   │   ├── ConfigurationFileWatcher.cs # 文件热更新
│   │   └── KestrelEndpointChangeDetector.cs # 端点变更检测
│   └── Alert/
│       ├── IGatewayAlertService.cs     # 告警接口
│       └── NullGatewayAlertService.cs  # 默认实现
├── Infrastructure/Auth/
│   └── TotpHelper.cs                   # TOTP 2FA 验证器
├── Modules/Dashboard/
│   ├── Controllers/DeploymentInfoController.cs # /api/deployment/* 接口
│   └── Views/Dashboard/Deployment.cshtml         # 运行模式展示页
└── wwwroot/js/modules/
    └── dashboard-deployment.js         # 前端模块

samples/SampleGateway/appsettings.examples/
├── appsettings.AllInOne.json           # 兼容模式示例
├── appsettings.Split.json              # 双端口拆分示例
├── appsettings.SplitWithHealth.json    # 含健康检查示例
├── appsettings.ProxyOnly.json          # 仅代理示例
└── README.md                           # 使用说明
```

### 📝 使用示例

```csharp
// Program.cs 一行启用
builder.Services.AddAneiangYarpDeployment(builder.Configuration);

// 中间件挂载（在 UseAneiangYarpDashboard 之前）
app.UseMiddleware<EndpointRouterMiddleware>();
app.UseMiddleware<HealthCheckMiddleware>();
app.UseAneiangYarpDashboard();
```

```json
// appsettings.json 双端口配置
{
  "Kestrel": {
    "Endpoints": {
      "Proxy":     { "Url": "http://0.0.0.0:80" },
      "Dashboard": { "Url": "http://127.0.0.1:5000" }
    }
  },
  "Gateway": {
    "Deployment": {
      "Mode": "Split",
      "EndpointRoles": { "Proxy": "Proxy", "Dashboard": "Dashboard" }
    }
  }
}
```

### 🔗 相关文档
- 设计文档：`docs/灵活端口与启动方案设计.md`

#### Dashboard "运行模式" 页面
- 新增菜单项：**系统管理 → 运行模式**（路径：`/{prefix}/deployment`）
- 展示内容：
  - **当前模式卡片**：Auto / AllInOne / Split / ProxyOnly / DashboardOnly 五种模式高亮
  - **运行摘要**：进程启动时间、运行时长（实时更新）、程序版本、环境
  - **监听端点表格**：名称/地址/端口/角色/是否公网/状态
  - **配置热更新状态**：启用状态、监听文件、防抖时间、兜底轮询间隔、失败回滚
  - **健康检查状态**：启用状态、端点路径（`/health` `/ready` `/live`）、鉴权方式、检查项
  - **安全告警卡片**：Dashboard/Admin/Health 端口暴露公网时红色高亮
  - **配置快照表格**：时间/触发/文件/查看详情
  - **手动操作区**：重新加载配置、创建快照、健康检查
- 后端 API：`GET /api/deployment/summary` 聚合所有运行时信息
- 数据来源：`DeploymentOptions` + `EndpointRoleResolver` + `IConfigSnapshotStore`


## [2.3.0.20] - 2026-06-13

> 里程碑版本：完整 WAF 防火墙、通知告警系统、健康检查面板、熔断/限流/重试 Dashboard、Storage 模块架构重构（上帝接口消除）、SQLCipher 数据库加密、数据库下载。README 中英文版大幅扩容，新增技术栈总览、中间件管道、插件系统、策略引擎、性能优化等章节。

---

### 🛡️ 新增功能

#### WAF 防火墙（全新模块）
- **WAF 中间件引擎**（`WafMiddleware`，424 行）：在 YARP 代理管道前拦截恶意请求
  - **IP 黑白名单**：支持精确 IP、CIDR 网段、通配符，白名单优先策略，内置正则缓存优化
  - **SQL 注入检测**：2 组预编译正则（SQL 关键字 + 注入模式），带 5ms ReDoS 防护超时
  - **XSS 跨站脚本检测**：检测 `<script>` 标签、`javascript:` 伪协议、事件处理器注入
  - **路径遍历检测**：识别 `../` 及 URL 编码变体、双重编码攻击
  - **请求限制**：可配置请求体大小上限（默认 10MB）、请求头数量上限、单头大小上限、URI 长度上限（4096）
  - **安全响应头注入**：自动添加 `X-Content-Type-Options`、`X-Frame-Options`、`X-XSS-Protection`、`Referrer-Policy`、`Content-Security-Policy`
  - **按路由级精细控制**：通过 YARP 路由元数据 `Waf:Enabled` 可单独为每个路由启用/禁用 WAF
  - 拦截时返回 403 JSON（含 `waf:true` 标记便于前端识别）
- **WAF 设置管理页面**（`Views/Dashboard/Waf.cshtml`）：全局开关、检测规则开关、IP 黑白名单 textarea、请求限制参数配置
- **WAF 设置持久化**（`WafSettingsPersistenceService`）：带内存缓存的读写分离，`SemaphoreSlim` 线程安全，`appsettings.json` 兜底
- **安全事件查看器**（`Views/Dashboard/Security.cshtml` + `dashboard-security.js`）：15 秒自动刷新，按事件类型过滤，攻击类型统计条形图，Top 攻击 IP 排行
- **安全事件环形缓冲区**（`WafEventStore`，`ConcurrentQueue`）：内存保留最近 1000 条，支持获取和清空
- **SecurityEventsController API**：`/api/security-events` — 最近事件查询、清空、测试事件触发、批量测试、统计摘要

#### 通知告警系统（全新模块）
- **多渠道通知**：支持配置多个 Webhook 端点，独立超时、重试次数设置
- **告警规则引擎**：按事件类型独立开关（熔断器打开、重试耗尽、WAF 拦截、代理错误、限流触发）
- **配置变更通知**：路由/集群增删改、重命名、配置回滚等操作自动产生通知事件
- **告警冷却机制**：同一告警在冷却时间内不重复触发，避免告警风暴
- **事件分发架构**：
  - `IConfigChangeNotifier` 接口：纯事件通知职责
  - `ConfigChangeEventDispatcher`（`BackgroundService`）：队列解耦，3 秒延迟启动，200ms 轮询出队分发
  - `PendingNotification` 值类型（`readonly struct`）入队，零分配压力
- **通知历史记录**：内存环形缓冲区 + SQLite 持久化，支持分页查询和按事件类型过滤
- **通知设置页面**：Webhook 端点管理、告警规则开关、高级设置（超时/重试/冷却/历史上限）
- **默认通知规则种子**：`StartupWarmupService` 启动时自动创建 11 种事件类型的默认规则

#### 健康检查面板
- **集群健康概览页面**（`Views/Dashboard/HealthCheck.cshtml`）：所有集群健康状态总览、目标节点健康评分
- **异常目标节点详情**：展示异常节点地址、状态、失败原因，支持仅显示异常过滤
- **健康检查配置模型**：`ActiveHealthCheckConfig`（主动检查：间隔/超时/策略/路径）、`PassiveHealthCheckConfig`（被动检查：策略/恢复周期）、`ClusterHealthCheckConfig`（组合配置）
- **HealthCheckController API**：`/api/health-check/clusters` 集群健康配置，`/api/health-check/status` 实时健康状态
- **`DefaultHealthCheckService`**：启动时为所有集群自动应用默认被动健康检查策略

#### 熔断器状态面板
- **Dashboard 熔断器查看页**（`Views/Dashboard/CircuitBreaker.cshtml`）：实时展示各集群/节点的熔断状态（Closed/Open/HalfOpen）
- 显示连续失败次数 vs 阈值、恢复超时倒计时、熔断时间、最后访问时间
- 支持一键重置所有熔断器（带确认对话框）

#### 限流状态面板
- **Dashboard 限流查看页**：展示固定窗口、滑动窗口、令牌桶、并发限制四种限流策略的实时状态
- 显示限制数、时间窗口、活跃请求数、已拒绝数、总请求数

#### 设置页面增强
- **数据库下载功能**：在设置页面提供一键下载 SQLite 数据库文件到本地，方便使用数据库工具（DB Browser for SQLite 等）直接查看
- **DashboardController.DownloadDatabase**：解析 `ConnectionString` 中的 `Data Source` 路径，支持相对路径自动转绝对路径，返回 `PhysicalFile` 流式下载

#### SQLite 数据库加密
- 集成 `SQLitePCLRaw.bundle_e_sqlcipher`，支持 SQLCipher AES-256 加密
- 连接字符串添加 `Password=xxx` 即可启用加密（`Data Source=gateway-store.db;Password=MyP@ss`）
- 加密对上层代码完全透明，Repository、下载接口等无需任何改动

#### Agent 自动注册增强
- **HTTP/gRPC 双协议注册**：`GatewayAutoRegistrationClient` 同时支持 HTTP REST API 和 gRPC（`GatewayRegistryClient`）注册
- **指数退避重试**：启动时最多 5 次重试（2s → 4s → 8s → 16s → 30s），提高首次注册成功率
- **自动端口分配**：gRPC 端口 = HTTP 端口 + 1，Kestrel 自动配置 HTTP/1.1 + HTTP/2（h2c）
- **IP 自动解析**：多源检测（Kestrel:EndPoints > Urls > ASPNETCORE_URLS），`KestrelAutoConfigService` 检测 `0.0.0.0` 监听
- **多种认证支持**：Bearer Token、Basic Auth、API Key 三种认证方式
- **智能默认值**：`RegistrationOptionsResolver` 自动推断 RouteName（入口程序集名）、MatchPath（`/{**catch-all}`）、DestinationAddress

#### gRPC 注册接口
- **`Aneiang.Yarp.Grpc` 项目**：`GatewayRegistry.proto` 定义 gRPC 服务契约（`RegisterService`、`UnregisterService`、`Heartbeat`）
- `GatewayRegistryService` 服务端实现：gRPC 注册的路由自动标记来源，支持幂等覆盖注册

---

### 🏗️ 架构重构

#### Storage 模块接口隔离（上帝接口/上帝类消除）
- **`SqliteConnectionFactory`**：共享 SQLite 连接工厂单例，所有仓储复用同一连接池，双检锁确保 `SQLitePCL.Batteries_V2.Init()` 仅初始化一次
- **8 个独立仓储接口**替代原 `IGatewayRepository` 上帝接口：
  - `IRouteRepository`、`IClusterRepository`、`IPolicyRepository`、`IConfigHistoryRepository`
  - `IAuditLogRepository`、`IWafSettingsRepository`、`IProxyLogRepository`、`INotificationRepository`
- **8 个 SQLite 独立仓储实现**：每个仓储仅管理自身表结构，职责单一
- **懒加载建表机制**：`EnsureInitializedAsync()` 双检锁（`lock` + `_initialized` 标记），首次使用时自动建表，不阻塞启动
- **消费方依赖精简**：6 个核心服务从注一整个上帝接口改为只注入需要的 1-2 个子接口
- **DI 统一注册**：`StorageServiceCollectionExtensions.AddAneiangStorage()` 集中注册所有仓储 + 连接工厂

#### 事件架构改进
- **审计与通知职责分离**：`IConfigChangeNotifier`（纯事件）← `IConfigChangeAuditLog`（审计+事件），`ConfigChangeAuditLog` → `ConfigChangeEventDispatcher` 队列分发
- **多级缓存架构**：WAF 设置/动态配置/通知设置 均采用 Python 内存缓存 + `SemaphoreSlim(1,1)` 线程安全双检锁模式

#### 预热/预加载机制
- **`StartupWarmupService`**：应用启动时 `Task.WhenAll` 并行预热 4 大模块（仓库建表、查询缓存、代理日志存储、通知规则种子），秒表计时日志
- **`NotificationWarmupService`**：独立预热通知系统，调用 `PreloadAsync()` 加载渠道/规则/设置到内存缓存
- 错误隔离：每个预热任务静默捕获异常，不影响应用启动

#### 模型和实体层整理
- **实体类拆分为独立文件**：`RouteEntity`、`ClusterEntity`、`DestinationEntity`、`ConfigHistoryEntity`、`PolicyEntity`、`AuditLogEntity`、`ProxyLogEntity`
- **新增模型文件**：`ActiveHealthCheckConfig`、`PassiveHealthCheckConfig`、`ClusterHealthCheckConfig`、`HealthCheckConfig`、`CircuitBreakerConfig`
- **`ConfigEntityMapper`**：静态扩展方法在领域模型与存储实体间转换，统一 `System.Text.Json` camelCase 序列化选项
- **按模块组织模型**：Dashboard 项目的 DTO/模型分散在各 `Modules/*/Models/` 下，按功能边界拆分

#### 配置系统优化
- 统一 `Gateway:{Subsystem}` 命名规范（`Gateway:Storage`、`Gateway:Dashboard`、`Gateway:Dashboard:Waf`）
- `PostConfigure<WafOptions>` 联动：Dashboard 路由前缀自动注入 WAF 选项
- 双重注册模式：关键服务同时注册接口和具体类型（如 `IConfigChangeAuditLog` / `ConfigChangeAuditLog`），均解析为同一单例

---

### 🐛 修复

- **`DynamicYarpConfigService` 线程安全修复**：`ReaderWriterLockSlim`（不支持 await）→ `SemaphoreSlim(1,1)`，解决异步方法内的线程亲和性问题
- **`TrySetRouteDisabled`/`TrySetClusterDisabled` 元数据同步**：修复启用/禁用路由或集群时内存元数据不同步的 bug
- **CS0121 二义性修复**：`ConfigPersistenceService` 中 `EntityMapper` 与新增 `ConfigEntityMapper` 命名冲突，通过显式命名空间调用解决
- **`SqliteConnectionFactory` 命名空间修正**：修复 `StorageOptions` 在特定编译上下文下解析失败的问题

---

### 🔧 移除

- `IGatewayRepository.cs` — 上帝接口（聚合 9 个子接口 + `IAsyncDisposable`）
- `SqliteGatewayRepository.cs` — 970 行上帝类，所有仓储逻辑耦合在同一文件中
- `RedisGatewayRepositoryPlaceholder.cs` — 所有方法均抛 `NotImplementedException` 的死代码
- `IDataStore.cs`、`IStructuredDataStore.cs` — 遗留抽象接口，已被独立仓储取代
- `IDynamicConfigPersistenceService.cs` / `DynamicConfigPersistenceService.cs` — 职责分散到 `IRouteRepository` + `IClusterRepository`
- `DynamicConfigPreloadService.cs` / `WebhookSettingsPreloadService.cs` — 预加载逻辑合并入 `StartupWarmupService`
- `SqliteDataStore.cs`、`RedisDataStore.cs`、`StructuredSqliteStore.cs` — 旧数据存储实现
- `GatewayRepositoryAdapter.cs` — 适配器层，接口隔离后不再需要

---

### 🌐 国际化

新增 120+ 中英文 i18n 键值对，覆盖：
- WAF 防火墙全部 UI（全局开关、检测规则、IP 名单、保存/加载状态）
- 通知告警全部 UI（渠道管理、告警规则、高级设置、事件类型描述）
- 安全事件查看器
- 健康检查面板（集群列表、目标节点、异常详情、状态标签）
- 熔断器面板（状态名称、操作确认、阈值显示）
- 限流面板（策略名称、窗口参数、统计数据）
- 数据库下载（按钮、描述、下载中/完成/失败提示）

---

## [2.3.0.10] - 2026-05-24

### 新增

- 添加审计日志功能，记录所有配置变更操作（操作类型、操作对象、操作者、变更前后 JSON、时间戳）
- 添加 `Aneiang.Yarp.Client` 客户端 SDK，支持微服务一行代码自动注册到网关

### 优化

- 优化配置回滚功能，回滚逻辑更可靠
- 优化日志展示页面交互体验
- 优化代码结构，消除编译警告
- 更新 README 和项目文档

---

## [2.3.0.9] - 2026-05-19

### 新增

- 支持 HTTPS 监听，客户端自动注册支持 HTTPS 地址

---

## [2.3.0.8] - 2026-05-19

### 优化

- 更新 README 文档

---

## [2.3.2.7] - 2026-05-19

### 新增

- 添加 IP 隔离负载均衡策略（`IpBasedLoadBalancingPolicy`），启用后多个开发者共用同一路由路径，网关按请求来源 IP 自动路由，前端完全无感知

---

## [2.3.0.6] - 2026-05-19

### 新增

- 添加 `IWebHostBuilder` 适配，支持传统 ASP.NET Core 宿主模式

---

## [2.3.0.5] - 2026-05-19

### 修复

- 修复客户端自动注册无法正常开启 `0.0.0.0` 监听的问题

---

## [2.3.0.4] - 2026-05-15

### 修复

- 修复 Dashboard 前端资源无法访问的问题

---

## [2.3.0.3] - 2026-05-06

### 新增

- 添加配置导入导出功能，支持标准 YARP 格式互转
- 自动监听 `0.0.0.0`，无需手动配置 Kestrel 地址

### 优化

- 优化代码结构和稳定性

---

## [2.3.0.2] - 2026-04-30

### 新增

- 添加多语言支持（中文/英文运行时切换）

### 修复

- 修复日志滚动条显示问题

### 优化

- 将前端 CDN 资源改为本地加载，离线环境可用
- 优化日志模块性能
- 优化代码结构

---

## [2.3.0] - 2026-04-28

### 新增

- 添加请求日志监控功能，支持实时查看经过网关的请求/响应详情
- 添加日志脱敏和采样机制（Header 黑名单、JSON 字段脱敏、采样率控制）
- 添加 WebSocket 实时日志推送

### 优化

- 优化日志监听中间件

---

## [2.0.0] - 2026-04-28

### 新增

- 初始版本发布
- 基于 YARP 2.3.0 构建
- 可视化 Dashboard 管理面板（RCL 嵌入式架构）
- 动态路由和集群配置管理（运行时 CRUD，无需重启）
- 配置快照与一键回滚
- 原子文件持久化（`gateway-dynamic.json`）
- 四种认证模式（None / DefaultJwt / CustomJwt / ApiKey）
- 路由前缀可配置（`IApplicationModelConvention` 注入）
- `ReaderWriterLockSlim` 线程安全并发控制
- 示例项目（SampleGateway + SampleLocalService）
- MIT 开源协议

---

[GitHub](https://github.com/AneiangSoft/Aneiang.Yarp) | [Gitee](https://gitee.com/aneiangsoft/aneiang-yarp)
