# 更新日志


## [2.3.0.11] - 2026-06-11

### 新增

- 添加独立仓储接口（`IRouteRepository`、`IClusterRepository`、`IConfigHistoryRepository`、`IPolicyRepository`、`IAuditLogRepository`、`IWafSettingsRepository`、`IProxyLogRepository`、`INotificationRepository`）——接口隔离，按领域职责拆分
- 添加 `IConfigChangeNotifier` 接口，从 `IConfigChangeAuditLog` 中拆出事件通知职责
- 添加 `ConfigEntityMapper` 核心映射器（供 `DynamicYarpConfigService` 使用）
- 将实体类拆分为独立文件（`RouteEntity`、`ClusterEntity`、`DestinationEntity`、`ConfigHistoryEntity`、`PolicyEntity`、`AuditLogEntity`、`WebhookSettingsEntity`、`ProxyLogEntity`）
- 新增 `ActiveHealthCheckConfig.cs`、`PassiveHealthCheckConfig.cs` 独立模型文件

### 变更

- `DynamicYarpConfigService`：依赖从 `IDynamicConfigPersistenceService` 改为 `IRouteRepository` + `IClusterRepository`；修复 `ReaderWriterLockSlim` → `SemaphoreSlim(1,1)` 解决 await 线程亲和性问题；修复 `TrySetRouteDisabled`/`TrySetClusterDisabled` 使用 `_dynamicConfig` 保持元数据同步
- `ConfigPersistenceService`：依赖从 `IStructuredDataStore` + `IDynamicConfigPersistenceService` 改为 `IConfigHistoryRepository` + `IRouteRepository` + `IClusterRepository`
- `ConfigChangeAuditLog`：依赖从 `IStructuredDataStore` 改为 `IAuditLogRepository`
- `GatewayPolicyService`：依赖从 `IStructuredDataStore` 改为 `IPolicyRepository`
- `WafSettingsPersistenceService`：依赖从 `IStructuredDataStore` 改为 `IWafSettingsRepository`
- `ConfigSnapshotService`：依赖从 `IStructuredDataStore` 改为 `IConfigHistoryRepository`
- `StorageServiceCollectionExtensions`：移除 `IStructuredDataStore` 遗留注册；直接注册 8 个独立仓储 + `SqliteConnectionFactory`
- `DashboardServiceCollectionExtensions`：移除 `IDynamicConfigPersistenceService`、`DynamicConfigPreloadService`、`WebhookSettingsPreloadService` 和 `INotificationRepository` 的 `IGatewayRepository` 转发注册

### 重构：Storage 模块接口隔离（上帝接口/上帝类消除）

- **新增** `SqliteConnectionFactory`：共享 SQLite 连接工厂，所有仓储复用同一个连接池，避免重复初始化 provider
- **新增** 8 个独立 SQLite 仓储：`SqliteRouteRepository`、`SqliteClusterRepository`、`SqliteConfigHistoryRepository`、`SqlitePolicyRepository`、`SqliteAuditLogRepository`、`SqliteWafSettingsRepository`、`SqliteProxyLogRepository`、`SqliteNotificationRepository`
- **新增** 各仓储懒加载建表机制：`EnsureInitializedAsync()` 双检锁确保每个仓储首次使用时自行建表
- **变更** 所有消费方服务从 `IGatewayRepository` 改为依赖具体子接口：
  - `DynamicYarpConfigService` → `IRouteRepository` + `IClusterRepository`
  - `ConfigPersistenceService` → `IConfigHistoryRepository` + `IRouteRepository` + `IClusterRepository`
  - `ConfigChangeAuditLog` → `IAuditLogRepository`
  - `GatewayPolicyService` → `IPolicyRepository`
  - `WafSettingsPersistenceService` → `IWafSettingsRepository`
  - `ConfigSnapshotService` → `IConfigHistoryRepository`
- **变更** `StartupWarmupService`：逐个预热子仓储触发建表；`INotificationRepository` 独立解析
- **变更** `StorageServiceCollectionExtensions`：直接注册 8 个子仓储 + `SqliteConnectionFactory` 单例
- **变更** `DashboardServiceCollectionExtensions`：移除 `INotificationRepository` 的 `IGatewayRepository` 转发注册
- **变更** `ConfigPersistenceService`：`EntityMapper` 显式调用消解与 `ConfigEntityMapper` 的 CS0121 二义性
- **变更** `SqliteConnectionFactory`：修正 `StorageOptions` 命名空间引用

### 移除

- 删除 `IGatewayRepository.cs`（上帝接口，聚合 9 个子接口 + `IAsyncDisposable`）
- 删除 `SqliteGatewayRepository.cs`（970 行上帝类）
- 删除 `RedisGatewayRepositoryPlaceholder.cs`（所有方法均抛 `NotImplementedException` 的死代码）
- 删除 `IDataStore.cs`（无实际使用者）
- 删除 `IStructuredDataStore.cs` 遗留接口
- 删除 `IDynamicConfigPersistenceService.cs` 及其实现 `DynamicConfigPersistenceService.cs`
- 删除 `DynamicConfigPreloadService.cs` 和 `WebhookSettingsPreloadService.cs`（预加载逻辑合并入仓储初始化）
- 删除 `SqliteDataStore.cs`、`RedisDataStore.cs`、`StructuredSqliteStore.cs` 旧实现
- 删除 `GatewayRepositoryAdapter.cs` 适配器

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
