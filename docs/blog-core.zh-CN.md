# YARP 动态路由管理怎么做？Aneiang.Yarp 核心模块架构深度解析

> **Aneiang.Yarp 源码解析系列（篇 02）**
> | [上一篇：客户端自动注册](./blog-client.zh-CN.md) | [下一篇：可视化 Dashboard](./blog-dashboard.zh-CN.md) | [系列目录](./series-index.zh-CN.md) |

YARP 原生依赖 `appsettings.json` 管理路由，改配置要重启。Aneiang.Yarp 在此基础上实现了完整的运行时动态管理 —— 线程安全、自动持久化、配置来源追踪、变更审计。

**本文你会了解到：**

- `AddAneiangYarp()` 一行代码背后注册了哪些服务
- 配置双写机制如何保证 YARP 运行时和持久化文件的一致性
- `ReaderWriterLockSlim` 在读多写少场景下的运用
- 原子文件写入如何防止断电导致配置损坏
- 环形缓冲区审计日志的线程安全实现
- API 鉴权的三级凭证解析和智能推断

---

## 一行代码背后的工作

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAneiangYarp();
```

这一行代码完成了 10 件事：

```
AddAneiangYarp()
  ├── 1. AddReverseProxy() — 加载 appsettings.json 的 ReverseProxy 节
  ├── 2. 替换 IProxyConfigProvider → InMemoryConfigProvider — 统一到内存管理
  ├── 3. 注册 DynamicYarpConfigService — 核心：动态路由/集群 CRUD
  ├── 4. 注册 DynamicConfigPersistenceService — 配置文件读写
  ├── 5. 注册 ConfigChangeAuditLog — 变更审计（环形缓冲区）
  ├── 6. 注册 IpBasedLoadBalancingPolicy — IP 隔离负载均衡
  ├── 7. 注册 BuiltinTransformOptions — 内置请求变换
  ├── 8. RateLimitConfigProvider — 限流参数
  ├── 9. 注册 GatewayConfigController — RESTful API
  └── 10. 内部注册 Client DI — 网关自身也可作为客户端
```

> **可扩展**：返回 `IReverseProxyBuilder`，高级用户可以继续定制 YARP：
> ```csharp
> var proxyBuilder = builder.Services.AddAneiangYarp();
> proxyBuilder.AddTransforms<MyCustomTransform>();
> ```

---

## 配置双写机制

`DynamicYarpConfigService` 是整个核心模块最关键的服务，它维护两份数据的同步：

```
┌─────────────────────────────┐
│  InMemoryConfigProvider     │ ← YARP 运行时读取
│  (IProxyConfigProvider)     │
│  ├── Routes                 │
│  └── Clusters               │
└──────────┬──────────────────┘
           │ ReaderWriterLockSlim 保护
           ↓
┌─────────────────────────────┐
│  GatewayDynamicConfig        │ ← 持久化模型（含元数据）
│  ├── Routes[]                │
│  │    └── Source, CreatedAt  │
│  └── Clusters[]              │
│       └── Source, CreatedAt  │
└──────────┬──────────────────┘
           │ 异步持久化（锁外执行）
           ↓
┌─────────────────────────────┐
│  gateway-dynamic.json       │ ← 文件持久化
└─────────────────────────────┘
```

### 线程安全：ReaderWriterLockSlim

```
读操作（查询路由/配置）：
  EnterReadLock() → 并发读取 → ExitReadLock()

写操作（注册/删除/更新）：
  EnterWriteLock() → 修改内存 → Update YARP → ExitWriteLock()
                                                ↓
                                          finally: SaveDynamicConfigAsync()（锁外）
```

**关键细节**：文件写入在锁释放之后异步执行。这意味着写操作不会因为磁盘 I/O 而长时间持有锁，最大化并发吞吐。

### 配置来源追踪

每个路由和集群都携带元数据，在 Dashboard 中一眼就能区分来源：

| Source | 来源 | 可通过 Dashboard 删除？ |
|--------|------|----------------------|
| `config` | `appsettings.json` 静态配置 | 不可以（重启恢复） |
| `auto-register` | 客户端 SDK 自动注册 | 可以 |
| `dynamic` | 通过 API 动态添加 | 可以 |
| `dashboard` | Dashboard 面板操作 | 可以 |

---

## 原子文件写入

`DynamicConfigPersistenceService` 的写入策略：

```csharp
// 不是直接写目标文件，而是先写临时文件再替换
var tmpPath = configFilePath + ".tmp";
await File.WriteAllTextAsync(tmpPath, json);
File.Move(tmpPath, configFilePath, overwrite: true); // NTFS/ext4 原子操作
```

**为什么不直接 `File.WriteAllText`？**

如果在写入过程中进程崩溃或断电，目标文件会处于半写状态（内容截断）。原子写入保证文件要么是旧的完整版本，要么是新的完整版本。

### 启动加载流程

```
应用启动
  ├── 1. LoadConfig() — 读 gateway-dynamic.json
  │     └── 文件不存在或格式错误 → 返回空配置（不中断启动）
  ├── 2. AddReverseProxy() — 加载 appsettings.json 的 ReverseProxy 节
  ├── 3. MarkStaticConfig() — 标记静态配置（Source = "config"）
  └── 4. 合并动态配置 — 同名路由静态优先，动态跳过
```

---

## 审计日志：环形缓冲区

每次配置变更都会记录审计，无需数据库，纯内存：

```csharp
public class ConfigChangeAudit
{
    public long Id { get; set; }            // 自增 ID
    public string Action { get; set; }      // "AddRoute" | "RemoveRoute" | ...
    public string Target { get; set; }      // 操作对象名称
    public string? Operator { get; set; }   // 操作者 IP 或用户名
    public bool Success { get; set; }       // 是否成功
    public string? Before { get; set; }     // 变更前 JSON
    public string? After { get; set; }      // 变更后 JSON
    public DateTime Timestamp { get; set; }
}
```

### 线程安全实现

```csharp
private readonly ConcurrentQueue<ConfigChangeAudit> _audits = new();
private const int MaxCapacity = 200;
private long _totalCount;    // Interlocked.Increment
private long _evictedCount;  // 超过容量时 TryDequeue 淘汰
```

- `ConcurrentQueue` — 无锁并发写入
- 超过 200 条时 `TryDequeue` 淘汰最旧记录
- `Interlocked` 保证计数原子性
- 零外部依赖，不需要数据库或 Redis

所有变更方法都接入了审计（`TryAddRoute`、`TryRemoveRoute`、`TryAddCluster`、`TryRemoveCluster`、`TryRenameCluster`、`TryUpdateCluster`、`ReplaceAllConfig`），成功和失败路径都记录。

---

## RESTful API

`GatewayConfigController` 提供 `api/gateway` 前缀的完整 API：

### 路由注册（幂等操作）

```http
POST /api/gateway/register-route
{
  "routeName": "my-service",
  "clusterName": "my-service",
  "matchPath": "/api/my-service/{**catch-all}",
  "destinationAddress": "http://192.168.1.20:5001",
  "useIpIsolation": false
}
```

智能行为：集群不存在时自动创建，路由已存在时更新。

### CRUD 端点

```http
GET    /api/gateway/routes              # 所有路由
GET    /api/gateway/dynamic-config      # 完整动态配置（含元数据）
PUT    /api/gateway/routes/{routeId}    # 更新路由
POST   /api/gateway/clusters            # 创建集群
PUT    /api/gateway/clusters/{id}       # 更新集群
DELETE /api/gateway/clusters/{id}       # 删除集群
DELETE /api/gateway/{routeName}?clientIp=  # IP 隔离模式精确注销
GET    /api/gateway/ping                # 健康检查
POST   /api/gateway/heartbeat           # 心跳上报
```

---

## API 鉴权：智能凭据推断

```csharp
builder.Services.AddGatewayApiAuth();
```

三级凭证解析，优先级从高到低：

```
1. 代码回调 — AddGatewayApiAuth(configure => { ... })
2. 配置文件 — Gateway:ApiAuth 节
3. Dashboard JWT 密码 — 自动推断（BasicAuth, username=admin）
```

**最大亮点**：如果你配了 Dashboard 的 JWT 密码，API 鉴权自动可用，不需要重复配置：

```json
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "demo123"
    }
    // 不需要 Gateway:ApiAuth 节
  }
}
```

### 生产环境：安全关闭注册 API

```csharp
builder.Services.AddAneiangYarp(enableRegistration: false);
```

通过 MVC Convention 移除 `register-route` 和 `heartbeat` 端点，访问返回 **404**（而非 403）——从外部看就像这些端点不存在。

---

## 示例：SampleGateway

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAneiangYarp();          // 网关（加载静态 + 动态配置）
builder.Services.AddAneiangYarpDashboard(); // Dashboard

var app = builder.Build();
app.UseRouting();
app.UseAneiangYarpDashboard();
app.MapControllers();
app.MapReverseProxy();
app.Run();
```

```json
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
    "Dashboard": { "AuthMode": "DefaultJwt", "JwtPassword": "demo123" }
  }
}
```

展示了三个实际场景：精确路由优先匹配（Order=2）、通配路由兜底（Order=100）、多实例负载均衡。

---

## 架构设计问答

**Q：为什么用 InMemoryConfigProvider 而不是自定义 IProxyConfigProvider？**

YARP 的 `IProxyConfigProvider` 需要实现 `IChangeToken` 变更通知。`InMemoryConfigProvider` 是内置实现，调 `Update()` 就能触发热更新，省去了大量样板代码。

**Q：为什么用 ReaderWriterLockSlim 而不是其他并发方案？**

YARP 的 `InMemoryConfigProvider.Update()` 不是线程安全的。`ReaderWriterLockSlim` 在读多写少场景下效率高 —— 多个查询并发读不阻塞，写操作独占。

**Q：配置持久化为什么放在锁外？**

磁盘 I/O 可能需要几十毫秒。放在锁内会阻塞所有写操作。放在锁外是"尽力而为"策略 —— 即使写入失败，内存中的配置仍然正确，下次变更时会重新写入。

---

## 设计总结

| 设计点 | 方案 | 优势 |
|--------|------|------|
| 配置管理 | InMemoryConfigProvider + 双写 | 兼容 YARP 生态，运行时热更新 |
| 线程安全 | ReaderWriterLockSlim | 读多写少高效 |
| 文件持久化 | 原子写入（tmp + move） | 防崩溃损坏 |
| 审计日志 | ConcurrentQueue + 环形缓冲 | 零外部依赖，线程安全 |
| API 鉴权 | 三级凭证 + 智能推断 | 配置最少，安全最大 |
| 注册安全 | MVC Convention 移除路由 | 404 而非 403 |

**源码地址**：[GitHub](https://github.com/AneiangSoft/Aneiang.Yarp) | [Gitee](https://gitee.com/aneiangsoft/aneiang-yarp)

```bash
dotnet add package Aneiang.Yarp
# Program.cs: builder.Services.AddAneiangYarp();
```

---

> **Aneiang.Yarp 源码解析系列**
>
> | [上一篇：客户端自动注册](./blog-client.zh-CN.md) | [下一篇：可视化 Dashboard](./blog-dashboard.zh-CN.md) | [系列目录](./series-index.zh-CN.md) |
>
> 觉得有用？去 [GitHub](https://github.com/AneiangSoft/Aneiang.Yarp) 或 [Gitee](https://gitee.com/aneiangsoft/aneiang-yarp) 点个 Star 支持一下。
