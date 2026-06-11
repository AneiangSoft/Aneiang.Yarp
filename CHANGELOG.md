# 更新日志


## [2.3.0.11] - 2026-06-11

### 新增

- 添加 `IGatewayRepository` 统一仓储抽象及子接口（`IRouteRepository`、`IClusterRepository`、`IConfigHistoryRepository`、`IPolicyRepository`、`IAuditLogRepository`、`IWebhookSettingsRepository`、`IProxyLogRepository`）
- 添加 `SqliteGatewayRepository` 作为默认 SQLite 仓储实现
- 添加 `RedisGatewayRepositoryPlaceholder` 骨架作 Redis 扩展预留
- 添加 `IConfigChangeNotifier` 接口，从 `IConfigChangeAuditLog` 中拆出事件通知职责
- 添加 `ConfigEntityMapper` 核心映射器（供 `DynamicYarpConfigService` 使用）
- 将实体类拆分为独立文件（`RouteEntity`、`ClusterEntity`、`DestinationEntity`、`ConfigHistoryEntity`、`PolicyEntity`、`AuditLogEntity`、`WebhookSettingsEntity`、`ProxyLogEntity`）
- 新增 `ActiveHealthCheckConfig.cs`、`PassiveHealthCheckConfig.cs` 独立模型文件

### 变更

- `DynamicYarpConfigService`：依赖从 `IDynamicConfigPersistenceService` 改为 `IGatewayRepository`；修复 `ReaderWriterLockSlim` → `SemaphoreSlim(1,1)` 解决 await 线程亲和性问题；修复 `TrySetRouteDisabled`/`TrySetClusterDisabled` 使用 `_dynamicConfig` 保持元数据同步
- `ConfigPersistenceService`：依赖从 `IStructuredDataStore` + `IDynamicConfigPersistenceService` 改为 `IGatewayRepository`
- `ConfigChangeAuditLog`：依赖从 `IStructuredDataStore` 改为 `IGatewayRepository`
- `GatewayPolicyService`、`GatewayPolicyPersistenceService`：依赖从 `IStructuredDataStore` 改为 `IGatewayRepository`
- `WebhookSettingsPersistenceService`：依赖从 `IStructuredDataStore` 改为 `IGatewayRepository`
- `ConfigSnapshotService`：依赖从 `IStructuredDataStore` 改为 `IGatewayRepository`
- `StorageServiceCollectionExtensions`：移除 `IStructuredDataStore` 遗留注册
- `DashboardServiceCollectionExtensions`：移除 `IDynamicConfigPersistenceService`、`DynamicConfigPreloadService`、`WebhookSettingsPreloadService` 的注册

### 移除

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
