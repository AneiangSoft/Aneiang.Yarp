# [系列 02] 动态路由 + 配置持久化 + 审计日志：Aneiang.Yarp 网关核心模块深度解析

> **Aneiang.Yarp 源码解析系列** — [上一篇：01 - 客户端自动注册](./blog-client.zh-CN.md) | [目录](./series-index.zh-CN.md) | [下一篇：03 - 可视化 Dashboard](./blog-dashboard.zh-CN.md)
>
> YARP 原生依赖 `appsettings.json` 管理路由，改配置要重启。Aneiang.Yarp 在此基础上实现了完整的运行时动态管理——线程安全、自动持久化、配置来源追踪、变更审计。本文深入分析其核心架构设计。

---

## 一、核心入口：`AddAneiangYarp()`

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAneiangYarp();
```

这一行代码背后，`AneiangYarpServiceCollectionExtensions.AddAneiangYarp()` 完成了以下工作：

```
AddAneiangYarp()
  │
  ├── 1. builder.Services.AddReverseProxy()
  │     └── 加载 appsettings.json 中的 ReverseProxy 节（静态配置）
  │
  ├── 2. 替换 IProxyConfigProvider → InMemoryConfigProvider
  │     └── 将静态配置加载到内存，后续所有变更都在内存中完成
  │
  ├── 3. 注册 DynamicYarpConfigService（核心服务）
  │     └── 动态路由/集群的 CRUD 全部走这里
  │
  ├── 4. 注册 DynamicConfigPersistenceService
  │     └── 负责配置的文件读写（原子写入）
  │
  ├── 5. 注册 ConfigChangeAuditLog
  │     └── 配置变更审计日志（环形缓冲区）
  │
  ├── 6. 注册 IpBasedLoadBalancingPolicy
  │     └── IP 隔离负载均衡策略
  │
  ├── 7. 注册 BuiltinTransformOptions
  │     └── 内置请求变换配置
  │
  ├── 8. RateLimitConfigProvider
  │     └── 从 Dashboard 配置读取限流参数
  │
  ├── 9. 注册 GatewayConfigController（API 端点）
  │     └── 可选：enableRegistration=false 时移除注册/心跳端点
  │
  └── 10. 内部注册 AneiangYarpClient 的 DI 服务
        └── 网关自身也可以作为客户端注册到上游网关
```

**设计亮点**：暴露 `IReverseProxyBuilder` 返回值，高级用户可以继续定制 YARP：

```csharp
var proxyBuilder = builder.Services.AddAneiangYarp();
proxyBuilder.AddTransforms<MyCustomTransform>();
```

---

## 二、配置双写机制：`DynamicYarpConfigService`

这是整个网关核心模块最复杂、最关键的服务。它维护两份数据的同步：

```
┌─────────────────────────────────┐
│   InMemoryConfigProvider         │  ← YARP 运行时读取
│   (IProxyConfigProvider)         │
│   ├── Routes                     │
│   └── Clusters                   │
└───────────┬─────────────────────┘
            │ lock 保护，原子更新
            ↓
┌─────────────────────────────────┐
│   GatewayDynamicConfig           │  ← 持久化模型（含元数据）
│   ├── Routes[]                   │
│   │    └── Source, CreatedAt...  │
│   └── Clusters[]                 │
│        └── Source, CreatedAt...  │
└───────────┬─────────────────────┘
            │ 异步持久化（锁外执行）
            ↓
┌─────────────────────────────────┐
│   gateway-dynamic.json           │  ← 文件持久化
└─────────────────────────────────┘
```

### 线程安全：ReaderWriterLockSlim

```
读操作（routes/dynamic-config 查询）：
  └── EnterReadLock() → 读取 → ExitReadLock()
      支持并发读，不阻塞其他读者

写操作（register/delete/update）：
  └── EnterWriteLock() → 修改 → Update YARP → ExitWriteLock()
                          │
                          └── finally: SaveDynamicConfigAsync()（锁外执行）
```

**关键细节**：文件写入在 `finally` 块中、锁释放之后异步执行。这意味着写操作不会因为磁盘 I/O 而长时间持有锁，最大化吞吐量。

### 配置来源追踪

每个路由和集群都携带元数据：

```csharp
public class RouteConfigMetadata
{
    public string? Source { get; set; }       // "config" | "dynamic" | "auto-register" | "dashboard"
    public DateTime CreatedAt { get; set; }   // 创建时间
    public string? CreatedBy { get; set; }    // 创建者（客户端 IP 或 Dashboard 用户）
    public DateTime? LastHeartbeat { get; set; } // 最后心跳时间（自动注册服务专用）
}
```

在 Dashboard 中，你可以一眼区分哪些路由是静态配置的、哪些是自动注册的、哪些是手动创建的。

来源值说明：

| Source 值 | 来源 | 可否通过 Dashboard 删除 |
|-----------|------|----------------------|
| `config` | appsettings.json 静态配置 | 不可以（重启会恢复） |
| `auto-register` | 客户端 SDK 自动注册 | 可以 |
| `dynamic` | 通过 API 动态添加 | 可以 |
| `dashboard` | 通过 Dashboard 面板操作 | 可以 |

---

## 三、配置持久化：原子写入

`DynamicConfigPersistenceService` 负责将内存中的配置写入文件：

```csharp
// 原子写入策略
var tmpPath = configFilePath + ".tmp";
await File.WriteAllTextAsync(tmpPath, json, cancellationToken);
File.Move(tmpPath, configFilePath, overwrite: true);
```

**为什么不用 `File.WriteAllText` 直接写？**

如果在写入过程中进程崩溃或断电，文件会处于半写状态（内容截断）。原子写入策略：

1. 先写入临时文件 `.tmp`
2. 确认写入完整后，`File.Move` 替换原文件

`File.Move` 在 NTFS/ext4 上是原子操作，要么完全成功，要么完全失败，不会出现半写状态。

### 启动加载流程

```
应用启动
  │
  ├── 1. LoadConfig() — 读取 gateway-dynamic.json
  │     └── 文件不存在或格式错误 → 返回空配置（不中断启动）
  │
  ├── 2. AddAneiangYarp() — 加载 appsettings.json 的 ReverseProxy 节
  │     └── 通过 YARP builder 加载到 InMemoryConfigProvider
  │
  ├── 3. MarkStaticConfig() — 标记静态路由/集群
  │     └── Source = "config"
  │
  └── 4. 合并动态配置（来自 gateway-dynamic.json）
        └── Source = "dynamic"
```

静态配置和动态配置**同名路由不会冲突**——静态配置优先，动态配置跳过。这样设计保证重启后 appsettings.json 的配置始终生效。

---

## 四、审计日志：`ConfigChangeAuditLog`

每次配置变更都会记录审计日志，无需数据库，纯内存存储：

```csharp
public class ConfigChangeAudit
{
    public long Id { get; set; }           // 自增 ID
    public string Action { get; set; }     // "AddRoute" | "RemoveRoute" | "UpdateCluster" ...
    public string Target { get; set; }     // 操作对象名称
    public string? Operator { get; set; }  // 操作者（IP 或用户名）
    public bool Success { get; set; }      // 是否成功
    public string? ErrorMessage { get; set; }
    public string? Before { get; set; }    // 变更前 JSON
    public string? After { get; set; }     // 变更后 JSON
    public DateTime Timestamp { get; set; } // 时间戳
}
```

### 环形缓冲区设计

```csharp
private readonly ConcurrentQueue<ConfigChangeAudit> _audits = new();
private const int MaxCapacity = 200;
private long _totalCount;
private long _evictedCount;
```

- 使用 `ConcurrentQueue` 保证线程安全
- 超过 200 条时 `TryDequeue` 淘汰最旧记录
- `Interlocked.Increment` 保证计数原子性
- 零外部依赖，不需要数据库或 Redis

### 审计覆盖范围

`DynamicYarpConfigService` 的所有变更方法都接入了审计：

| 方法 | 成功记录 | 失败记录 |
|------|---------|---------|
| `TryAddRoute` | ✅ 新增路由详情 | ✅ 冲突原因 |
| `TryRemoveRoute` | ✅ 被删除的路由 | ✅ 路由不存在 |
| `TryAddCluster` (2 个重载) | ✅ 新增集群详情 | ✅ 已存在/无效地址 |
| `TryRemoveCluster` | ✅ 被删除的集群 | ✅ 被路由引用/不存在 |
| `TryRenameCluster` | ✅ 旧名→新名 | ✅ 新名冲突/旧名不存在 |
| `TryUpdateCluster` (2 个重载) | ✅ 更新内容 | ✅ 不存在/无效地址 |
| `ReplaceAllConfig` | ✅ 回滚操作标记 | — |

在 Dashboard 的审计日志页面中，可以按时间、操作类型、操作者、成功/失败过滤查看。

---

## 五、RESTful API：`GatewayConfigController`

网关核心模块提供完整的 RESTful API，路径前缀为 `api/gateway`：

### 路由注册

```http
POST /api/gateway/register-route
Content-Type: application/json

{
  "routeName": "my-service",
  "clusterName": "my-service",
  "matchPath": "/api/my-service/{**catch-all}",
  "destinationAddress": "http://192.168.1.20:5001",
  "order": 50,
  "useIpIsolation": false,
  "clientIp": "192.168.1.20"
}
```

**智能行为**：
- 集群不存在时自动创建
- 路由已存在时更新（幂等操作）
- `useIpIsolation: true` 时在集群中创建 IP 绑定的 Destination

### 路由/集群 CRUD

```http
GET    /api/gateway/routes              # 获取所有路由（不含 Destination 详情）
GET    /api/gateway/dynamic-config      # 获取完整动态配置（含元数据、心跳时间）
PUT    /api/gateway/routes/{routeId}    # 更新路由配置
POST   /api/gateway/clusters            # 创建集群
PUT    /api/gateway/clusters/{id}       # 更新集群
DELETE /api/gateway/clusters/{id}       # 删除集群
```

### 路由删除（支持 IP 隔离）

```http
DELETE /api/gateway/{routeName}?clientIp=192.168.1.20
```

当启用 IP 隔离时，删除操作只移除对应 IP 的 Destination，而非整个路由。只有当集群中没有任何 Destination 时才删除集群和路由。

### 健康检查 & 心跳

```http
GET  /api/gateway/ping                              # 返回 "pong"
POST /api/gateway/heartbeat?routeName=my-service     # 更新心跳时间
```

---

## 六、API 鉴权：`AddGatewayApiAuth()`

管理 API 默认无认证。启用鉴权：

```csharp
builder.Services.AddGatewayApiAuth();
```

### 三级凭证解析

```
优先级从高到低：

1. 代码回调（最高优先级）
   AddGatewayApiAuth(configure => { ... })

2. 配置文件 Gateway:ApiAuth
   { "Mode": "BasicAuth", "Username": "admin", "Password": "xxx" }

3. Dashboard JWT 密码智能推断（最低优先级）
   自动读取 Gateway:Dashboard:JwtPassword
   推断为 BasicAuth(username="admin", password=JWT密码)
```

**智能推断**是最大的亮点——你配了 Dashboard 的 JWT 密码后，API 鉴权自动可用，不需要重复配置：

```json
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "demo123"
    }
    // 不需要 Gateway:ApiAuth 节！
  }
}
```

### 安全关闭注册 API

生产环境中，你可能只希望 Dashboard 来管理路由，而不是暴露注册 API：

```csharp
builder.Services.AddAneiangYarp(enableRegistration: false);
```

这会通过 `DisableRegistrationApiConvention`（MVC Convention）移除 `register-route` 和 `heartbeat` 端点。访问这些端点会返回 404 而非 403——从外部看就像这些端点不存在。

---

## 七、SampleGateway：完整示例

项目中的 `samples/SampleGateway` 展示了网关的完整用法：

```csharp
// samples/SampleGateway/Program.cs
using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 一行代码启用网关（自动加载静态配置 + 动态配置）
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
// samples/SampleGateway/appsettings.json
{
  "ReverseProxy": {
    "Routes": {
      "AuthServiceSelfRoute": {
        "ClusterId": "AuthServiceCluster",
        "Order": 2,
        "Match": { "Path": "/api/auth-service/{**catchAll}" }
      },
      "PlatFallbackRoute": {
        "ClusterId": "PlatServiceCluster",
        "Order": 100,
        "Match": { "Path": "/api/{**catchAll}" }
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

这个示例展示了几个实际场景：

1. **精确路由优先**（Order=1）：特定路径精确匹配，转发到认证服务并重写路径
2. **通配路由兜底**（Order=100）：未匹配的请求转发到平台服务
3. **多实例负载均衡**：AuthServiceCluster 配置了两个 Destination

---

## 八、架构设计要点

### 为什么用 InMemoryConfigProvider 而不是自定义 IProxyConfigProvider？

YARP 的 `IProxyConfigProvider` 接口要求实现配置变更通知（`IChangeToken`）。`InMemoryConfigProvider` 是 YARP 内置的实现，已经处理好了所有复杂性——只需要调用 `Update()` 方法即可触发配置热更新：

```csharp
_provider.Update(routeConfigs, clusterConfigs);
// YARP 内部自动触发 IChangeToken，所有依赖的中间件感知到变更
```

### 为什么用 lock 而不是其他并发方案？

YARP 的 `InMemoryConfigProvider.Update()` **不是线程安全的**。虽然读操作可以并发，但写操作必须串行化。`ReaderWriterLockSlim` 提供了：

- 读锁：多个读者并发，不阻塞
- 写锁：独占访问
- 可升级锁：读锁可升级为写锁（未使用，但保留扩展性）

### 配置持久化为什么放在 finally 块的锁外？

```csharp
try
{
    _writeLock.EnterWriteLock();
    // 修改内存配置
    _provider.Update(routes, clusters);
}
finally
{
    _writeLock.ExitWriteLock();
    // 锁外异步持久化——不阻塞下一个写操作
    _ = SaveDynamicConfigAsync();
}
```

磁盘 I/O 可能需要几十毫秒，如果放在锁内，高并发场景下会成为瓶颈。放在锁外，持久化是"尽力而为"——即使失败，内存中的配置仍然是正确的，下次变更时会重新写入。

---

## 总结

Aneiang.Yarp 网关核心模块的关键设计：

| 设计点 | 方案 | 优势 |
|--------|------|------|
| 配置管理 | InMemoryConfigProvider + 双写 | 兼容 YARP 生态，运行时热更新 |
| 线程安全 | ReaderWriterLockSlim | 读多写少场景高效 |
| 文件持久化 | 原子写入（tmp + move） | 防崩溃损坏 |
| 审计日志 | ConcurrentQueue + 环形缓冲 | 零外部依赖，线程安全 |
| API 鉴权 | 三级凭证解析 + 智能推断 | 配置最少，安全最大 |
| 注册 API 安全 | MVC Convention 移除路由 | 404 而非 403，更安全 |

---

## 项目信息

- **GitHub**: [https://github.com/aneiang/Aneiang.Yarp](https://github.com/aneiang/Aneiang.Yarp)
- **NuGet**: `dotnet add package Aneiang.Yarp`
- **协议**: MIT

```bash
dotnet add package Aneiang.Yarp
# Program.cs: builder.Services.AddAneiangYarp();
```

*（全文完）*

---

> **Aneiang.Yarp 源码解析系列** — [上一篇：01 - 客户端自动注册](./blog-client.zh-CN.md) | [目录](./series-index.zh-CN.md) | [下一篇：03 - 可视化 Dashboard](./blog-dashboard.zh-CN.md)
