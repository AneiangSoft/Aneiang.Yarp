# YARP 动态配置重构方案

> 总方案：消除影子状态 + 组合模式统一配置模型
> 适用项目：Aneiang.Yarp / Aneiang.Yarp.Dashboard

---

## 一、当前架构问题总结

```
SQLite ←→ GatewayDynamicConfig (影子状态)
                │  手动复制部分字段 + ConfigJson 字符串兜底
                ▼
          InMemoryConfigProvider (YARP 运行时)
```

| 问题 | 根因 |
|---|---|
| 字段丢失 | `DynamicRouteConfig` 缺 `MaxRequestBodySize`/`AuthorizationPolicy`/`CorsPolicy` 等，`ConfigJson` 反序列化失败时 fallback 丢失 |
| 双写同步 | `RouteId`/`ClusterId`/`MatchPath`/`Order`/`Transforms`/`Metadata` 同时存在于自定义字段和 `ConfigJson` |
| YARP 升级脆弱 | 新增字段无法感知 |
| 全局锁瓶颈 | `SemaphoreSlim(1,1)` 读写共用 |
| 启动复杂 | `MarkStaticConfig()` + `ValidateConsistency()` 修复双状态不一致 |
| 全量持久化 | Delete 操作走 O(N) 全量加载+删除+重存 |

---

## 二、目标架构

```
SQLite (持久化)
      ↕
AneiangProxyConfigProvider (唯一内存状态)
      │
      ├── AneiangProxyConfig (不可变快照)
      │       ├── Routes: IReadOnlyList<RouteConfig>              ← YARP 用
      │       ├── Clusters: IReadOnlyList<ClusterConfig>          ← YARP 用
      │       ├── DynamicRoutes: IReadOnlyList<DynamicRouteConfig>     ← Dashboard 用
      │       │       └── .Config === Routes[i]  (同一引用)
      │       ├── DynamicClusters: IReadOnlyList<DynamicClusterConfig> ← Dashboard 用
      │       │       └── .Config === Clusters[i]  (同一引用)
      │       └── ChangeToken  → YARP 热更新
      │
      └── Heartbeats: ConcurrentDictionary<string, DateTime>  (独立，不触发重载)
```

### 三个核心原则

1. **单一数据源** — 原生 `RouteConfig`/`ClusterConfig` 直接作为属性持有，不复制字段
2. **不可变快照** — 写操作构建新快照原子替换，读操作完全无锁
3. **增量持久化** — 所有操作走增量 DB 调用，消除全量 fallback

---

## 三、模型层重构（组合模式）

### 3.1 DynamicRouteConfig

```csharp
public sealed class DynamicRouteConfig
{
    /// <summary>完整的原生 YARP RouteConfig，包含所有字段（含未来新增）。</summary>
    public RouteConfig Config { get; set; } = new();

    // ── 扩展元数据（YARP 不关心，Dashboard/Middleware 需要）──
    public string RouteUid { get; set; } = Guid.NewGuid().ToString("N");
    public string Source { get; set; } = "dynamic";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    /// <summary>策略引擎注入的元数据，独立于 Config.Metadata。</summary>
    public Dictionary<string, string> PolicyMetadata { get; set; } = new();

    // ── 兼容属性（逐步迁移后删除）──
    public string RouteId
    {
        get => Config.RouteId ?? string.Empty;
        set => Config = Config with { RouteId = value };
    }
    public string ClusterId
    {
        get => Config.ClusterId ?? string.Empty;
        set => Config = Config with { ClusterId = value };
    }
    public string MatchPath
    {
        get => Config.Match?.Path ?? string.Empty;
        set => Config = Config with { Match = (Config.Match ?? new RouteMatch()) with { Path = value } };
    }
    public int Order
    {
        get => Config.Order ?? 50;
        set => Config = Config with { Order = value };
    }
    public List<Dictionary<string, string>>? Transforms
    {
        get => Config.Transforms?.Select(t => new Dictionary<string, string>(t)).ToList();
        set => Config = Config with { Transforms = value?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList() };
    }
    public string? ConfigJson
    {
        get => Serialization.YarpJsonConfig.SerializeRoute(Config);
        set { if (!string.IsNullOrEmpty(value)) Config = Serialization.YarpJsonConfig.DeserializeRoute(value); }
    }
}
```

消除的字段：`ClusterUid`、`DisplayName`、`Metadata`（合并到 `Config.Metadata` + `PolicyMetadata`）。

### 3.2 DynamicClusterConfig

```csharp
public sealed class DynamicClusterConfig
{
    /// <summary>完整的原生 YARP ClusterConfig。</summary>
    public ClusterConfig Config { get; set; } = new();

    // ── 扩展元数据 ──
    public string ClusterUid { get; set; } = Guid.NewGuid().ToString("N");
    public string Source { get; set; } = "dynamic";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public CircuitBreakerConfig? CircuitBreaker { get; set; }

    // ── 兼容属性 ──
    public string ClusterId
    {
        get => Config.ClusterId ?? string.Empty;
        set => Config = Config with { ClusterId = value };
    }
    public Dictionary<string, string> Destinations
    {
        get => Config.Destinations?.ToDictionary(d => d.Key, d => d.Value.Address ?? string.Empty) ?? new();
        set => Config = Config with { Destinations = value.ToDictionary(d => d.Key, d => new DestinationConfig { Address = d.Value }) };
    }
    public string? LoadBalancingPolicy
    {
        get => Config.LoadBalancingPolicy;
        set => Config = Config with { LoadBalancingPolicy = value };
    }
    public string? ConfigJson
    {
        get => Serialization.YarpJsonConfig.SerializeCluster(Config);
        set { if (!string.IsNullOrEmpty(value)) Config = Serialization.YarpJsonConfig.DeserializeCluster(value); }
    }
}
```

### 3.3 GatewayDynamicConfig — 保持兼容

```csharp
public class GatewayDynamicConfig
{
    public long Version { get; set; } = 1;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public List<DynamicRouteConfig> Routes { get; set; } = new();
    public List<DynamicClusterConfig> Clusters { get; set; } = new();
}
```

接口不变，`GetDynamicConfig()` 仍返回此类型，外部调用方零改动。

---

## 四、配置提供器

### 4.1 不可变快照

```csharp
internal sealed class AneiangProxyConfig : IProxyConfig
{
    public IReadOnlyList<RouteConfig> Routes { get; init; } = [];
    public IReadOnlyList<ClusterConfig> Clusters { get; init; } = [];
    public IChangeToken ChangeToken { get; init; } = new ConfigurationReloadToken();

    /// <summary>路由元数据。DynamicRoutes[i].Config 与 Routes[i] 是同一引用。</summary>
    public IReadOnlyList<DynamicRouteConfig> DynamicRoutes { get; init; } = [];
    /// <summary>集群元数据。DynamicClusters[i].Config 与 Clusters[i] 是同一引用。</summary>
    public IReadOnlyList<DynamicClusterConfig> DynamicClusters { get; init; } = [];

    public long Version { get; init; }
}
```

关键约束：构造时保证 `DynamicRoutes[i].Config` 与 `Routes[i]` 指向同一对象。

### 4.2 Provider 实现

```csharp
public sealed class AneiangProxyConfigProvider : IProxyConfigProvider
{
    private volatile AneiangProxyConfig _current;
    private ConfigurationReloadToken _reloadToken = new();
    private readonly ConcurrentDictionary<string, DateTime> _heartbeats = new();

    public AneiangProxyConfigProvider(
        IReadOnlyList<RouteConfig> initialRoutes,
        IReadOnlyList<ClusterConfig> initialClusters)
        => _current = BuildInitialSnapshot(initialRoutes, initialClusters);

    public AneiangProxyConfig Current => _current;
    public IProxyConfig GetConfig() => _current;
    public IReadOnlyList<RouteConfig> GetRoutes() => _current.Routes;
    public IReadOnlyList<ClusterConfig> GetClusters() => _current.Clusters;
    public ClusterConfig? GetCluster(string clusterId) =>
        _current.Clusters.FirstOrDefault(c => string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

    public GatewayDynamicConfig? GetDynamicConfig()
    {
        var snap = _current;
        return new GatewayDynamicConfig
        {
            Version = snap.Version,
            LastModified = DateTime.UtcNow,
            Routes = snap.DynamicRoutes.ToList(),
            Clusters = snap.DynamicClusters
                .Select(c => { c.LastHeartbeat = _heartbeats.GetValueOrDefault(c.ClusterId); return c; })
                .ToList()
        };
    }

    /// <summary>原子替换快照 + 触发 YARP 热更新。</summary>
    public void ApplySnapshot(AneiangProxyConfig snapshot)
    {
        _current = snapshot;
        var oldToken = Interlocked.Exchange(ref _reloadToken, new ConfigurationReloadToken());
        oldToken.OnReload();
    }

    public bool UpdateHeartbeat(string clusterId)
    {
        _heartbeats[clusterId] = DateTime.UtcNow;
        return true;
    }

    /// <summary>构建新快照（保证 Config 引用一致）。</summary>
    public AneiangProxyConfig CreateSnapshot(
        IReadOnlyList<DynamicRouteConfig> dynRoutes,
        IReadOnlyList<DynamicClusterConfig> dynClusters,
        long version)
        => new AneiangProxyConfig
        {
            Routes = dynRoutes.Select(r => r.Config).ToList(),
            Clusters = dynClusters.Select(c => c.Config).ToList(),
            DynamicRoutes = dynRoutes,
            DynamicClusters = dynClusters,
            Version = version,
            ChangeToken = _reloadToken
        };
}
```

---

## 五、DynamicYarpConfigService 重构

### 5.1 字段变化

```csharp
public class DynamicYarpConfigService : IDynamicYarpConfigService, IHostedService
{
    private readonly AneiangProxyConfigProvider _provider;   // 替代 InMemoryConfigProvider
    private readonly IRouteRepository _routeRepo;
    private readonly IClusterRepository _clusterRepo;
    private readonly IConfigChangeAuditLog _auditLog;
    private readonly ILogger<DynamicYarpConfigService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);   // 只保护写
    private long _configVersion;

    // 删除：InMemoryConfigProvider _configProvider; GatewayDynamicConfig _dynamicConfig;
    //       HashSet _staticRouteIds; HashSet _staticClusterIds;
}
```

### 5.2 读操作 — 完全无锁

```csharp
public IReadOnlyList<RouteConfig> GetRoutes() => _provider.GetRoutes();
public IReadOnlyList<ClusterConfig> GetClusters() => _provider.GetClusters();
public ClusterConfig? GetCluster(string clusterId) => _provider.GetCluster(clusterId);
public GatewayDynamicConfig? GetDynamicConfig() => _provider.GetDynamicConfig();
```

### 5.3 写操作 — 标准流程（以 TryAddRouteConfig 为例）

```csharp
public async Task<RouteOperationResult> TryAddRouteConfig(
    RouteConfig route, string source = "dashboard", string? createdBy = "dashboard-user")
{
    if (string.IsNullOrWhiteSpace(route.RouteId))
        return new RouteOperationResult(false, "Route ID cannot be empty");
    if (string.IsNullOrWhiteSpace(route.ClusterId))
        return new RouteOperationResult(false, "Cluster ID cannot be empty");

    await _writeLock.WaitAsync();
    try
    {
        var snap = _provider.Current;
        var existingIdx = snap.DynamicRoutes.FindIndex(r =>
            string.Equals(r.Config.RouteId, route.RouteId, StringComparison.OrdinalIgnoreCase));

        DynamicRouteConfig dynRoute;
        if (existingIdx >= 0)
        {
            var old = snap.DynamicRoutes[existingIdx];
            dynRoute = new DynamicRouteConfig
            {
                Config = route, RouteUid = old.RouteUid, Source = source,
                CreatedAt = old.CreatedAt, CreatedBy = old.CreatedBy,
                PolicyMetadata = old.PolicyMetadata  // 保留策略元数据
            };
        }
        else
        {
            dynRoute = new DynamicRouteConfig
            {
                Config = route, Source = source,
                CreatedAt = DateTime.UtcNow, CreatedBy = createdBy
            };
        }

        var newDynRoutes = snap.DynamicRoutes.ToList();
        if (existingIdx >= 0) newDynRoutes[existingIdx] = dynRoute;
        else newDynRoutes.Add(dynRoute);

        var newSnapshot = _provider.CreateSnapshot(newDynRoutes, snap.DynamicClusters, ++_configVersion);
        _provider.ApplySnapshot(newSnapshot);          // 原子替换 + YARP 热更新
        await _routeRepo.SaveRouteAsync(dynRoute.ToEntity());  // 增量持久化

        var action = existingIdx >= 0 ? "updated" : "registered";
        _auditLog.RecordSuccess(existingIdx >= 0 ? "UpdateRoute" : "AddRoute",
            route.RouteId, createdBy, null, null,
            new { clusterId = route.ClusterId, matchPath = route.Match?.Path });
        return new RouteOperationResult(true, $"Route '{route.RouteId}' {action}");
    }
    finally { _writeLock.Release(); }
}
```

### 5.4 所有写方法对照

| 方法 | 快照构建方式 | 持久化方式 |
|---|---|---|
| `TryAddRouteConfig` | 替换/添加路由项 | `SaveRouteAsync` (增量) |
| `TryAddClusterConfig` | 替换/添加集群项 | `SaveClusterAsync` + `SaveDestinationsAsync` (增量) |
| `TryRemoveRoute` | 移除路由项 + 条件移除集群项 | `DeleteRouteAsync` + 条件 `DeleteClusterAsync` (增量) |
| `TryRemoveCluster` | 移除集群项（校验无引用） | `DeleteClusterAsync` + `DeleteDestinationsAsync` (增量) |
| `TryRenameCluster` | 新建集群项 + 更新引用路由项 + 移除旧集群项 | `SaveClusterAsync` + `SaveRoutesAsync` + `DeleteClusterAsync` (增量) |
| `TryRenameRoute` | 新建路由项 + 移除旧路由项 | `SaveRouteAsync` + `DeleteRouteAsync` (增量) |
| `ReplaceAllConfig` | 全新快照 | 单事务全量替换 |
| `UpdateRouteMetadataAsync` | 替换路由项（合并 PolicyMetadata） | `SaveRouteAsync` (增量) |
| `UpdateClusterCircuitBreakerAsync` | 替换集群项 | `SaveClusterAsync` (增量) |
| `UpdateHeartbeat` | 不替换快照 | `ConcurrentDictionary` (无 DB) |

### 5.5 启动流程

```csharp
Task IHostedService.StartAsync(CancellationToken ct)
{
    LoadAndApplyConfig();
    return Task.CompletedTask;
}

private void LoadAndApplyConfig()
{
    // 1. 从 SQLite 加载
    var routeEntities = _routeRepo.GetAllRoutesAsync().GetAwaiter().GetResult();
    var clusterEntities = _clusterRepo.GetAllClustersAsync().GetAwaiter().GetResult();

    var dynRoutes = routeEntities.Select(e => e.ToRouteConfig()).ToList();
    var dynClusters = new List<DynamicClusterConfig>();
    foreach (var ce in clusterEntities)
    {
        var dyn = ce.ToClusterConfig();
        var dests = _clusterRepo.GetDestinationsAsync(ce.ClusterId).GetAwaiter().GetResult();
        dyn.Config = dyn.Config with
        {
            Destinations = dests.ToDictionary(d => d.DestinationId,
                d => new DestinationConfig { Address = d.Address })
        };
        dynClusters.Add(dyn);
    }

    // 2. 合并静态配置（Provider 构造时已从 appsettings.json 加载）
    MergeStaticConfig(dynRoutes, dynClusters);

    // 3. 构建快照并应用
    var snapshot = _provider.CreateSnapshot(dynRoutes, dynClusters, ++_configVersion);
    _provider.ApplySnapshot(snapshot);

    // 4. 持久化合并后状态
    PersistAllToRepository(dynRoutes, dynClusters);
}
```

消除的代码：`ApplyDynamicConfigToYarp()`、`ValidateConsistency()`、`EnsureDynamicConfigInitialized()`、`BuildRouteConfig()`/`BuildClusterConfig()`、`MarkStaticConfig()` 中的 RemoveAll + 重新同步逻辑。

---

## 六、持久化层适配

### 6.1 ConfigEntityMapper 简化

```csharp
public static RouteEntity ToEntity(this DynamicRouteConfig route) => new()
{
    RouteUid = route.RouteUid,
    RouteId = route.Config.RouteId,
    ClusterId = route.Config.ClusterId ?? string.Empty,
    MatchPath = route.Config.Match?.Path ?? string.Empty,
    Order = route.Config.Order ?? 50,
    Transforms = route.Config.Transforms is { Count: > 0 }
        ? JsonSerializer.Serialize(route.Config.Transforms, _jsonOptions) : null,
    Metadata = route.Config.Metadata is { Count: > 0 }
        ? JsonSerializer.Serialize(route.Config.Metadata, _jsonOptions) : null,
    Source = route.Source, CreatedBy = route.CreatedBy,
    CreatedAt = route.CreatedAt, UpdatedAt = DateTime.UtcNow,
    ConfigJson = YarpJsonConfig.SerializeRoute(route.Config)  // 完整序列化，自动含所有字段
};

public static DynamicRouteConfig ToRouteConfig(this RouteEntity entity)
{
    RouteConfig nativeConfig;
    if (!string.IsNullOrEmpty(entity.ConfigJson))
    {
        nativeConfig = YarpJsonConfig.DeserializeRoute(entity.ConfigJson);  // 优先完整字段
    }
    else
    {
        // fallback：从分散字段重建（兼容旧数据）
        nativeConfig = new RouteConfig
        {
            RouteId = entity.RouteId,
            ClusterId = entity.ClusterId,
            Match = new RouteMatch { Path = entity.MatchPath },
            Order = entity.Order,
            Transforms = entity.Transforms is { Length: > 0 }
                ? JsonSerializer.Deserialize<List<Dictionary<string, string>>>(entity.Transforms, _jsonOptions)
                  ?.Select(t => (IReadOnlyDictionary<string, string>)t).ToList() : null,
            Metadata = entity.Metadata is { Length: > 0 }
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(entity.Metadata, _jsonOptions) : null
        };
    }
    return new DynamicRouteConfig
    {
        Config = nativeConfig, RouteUid = entity.RouteUid,
        Source = entity.Source, CreatedBy = entity.CreatedBy, CreatedAt = entity.CreatedAt
    };
}
```

Cluster 映射同理。`ConfigJson` 列存的是原生类型的完整序列化，YARP 升级新增字段时自动保留，无需改代码。

### 6.2 增量持久化

```csharp
private async Task PersistRouteDeleteAsync(string routeId)
    => await _routeRepo.DeleteRouteAsync(routeId);

private async Task PersistClusterDeleteAsync(string clusterId)
{
    await _clusterRepo.DeleteDestinationsAsync(clusterId);
    await _clusterRepo.DeleteClusterAsync(clusterId);
}

// ReplaceAllConfig — 理想包裹单 SQLite 事务
private async Task PersistReplaceAllAsync(
    IReadOnlyList<DynamicRouteConfig> routes,
    IReadOnlyList<DynamicClusterConfig> clusters)
{
    await _routeRepo.SaveRoutesAsync(routes.Select(r => r.ToEntity()).ToList());
    await _clusterRepo.SaveClustersAsync(clusters.Select(c => c.ToEntity()).ToList());
    foreach (var c in clusters)
        await _clusterRepo.SaveDestinationsAsync(c.ClusterId,
            c.Config.Destinations?.Select(d => new DestinationEntity
            {
                DestinationId = d.Key, ClusterId = c.ClusterId, Address = d.Value.Address
            }) ?? []);
}
```

---

## 七、DI 注册变更

```csharp
// AneiangYarpServiceCollectionExtensions.cs

// 删除:
// services.AddSingleton<InMemoryConfigProvider>(...);
// services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<InMemoryConfigProvider>());

// 替换为:
services.AddSingleton<AneiangProxyConfigProvider>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var section = config.GetSection("ReverseProxy");
    return new AneiangProxyConfigProvider(
        YarpConfigParser.ParseRoutes(section.GetSection("Routes")),
        YarpConfigParser.ParseClusters(section.GetSection("Clusters")));
});
services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<AneiangProxyConfigProvider>());

// DynamicYarpConfigService 注入 AneiangProxyConfigProvider
services.AddSingleton<DynamicYarpConfigService>();
services.AddSingleton<IDynamicYarpConfigService>(sp => sp.GetRequiredService<DynamicYarpConfigService>());
```

---

## 八、影响范围

### 新增文件（2 个）

| 文件 | 说明 |
|---|---|
| `AneiangProxyConfig.cs` | 不可变快照模型（internal） |
| `AneiangProxyConfigProvider.cs` | IProxyConfigProvider 实现 |

### 修改文件（5 个）

| 文件 | 改动 |
|---|---|
| `DynamicRouteConfig.cs` | 组合模式 + 兼容属性 |
| `DynamicClusterConfig.cs` | 组合模式 + 兼容属性 |
| `DynamicYarpConfigService.cs` | 消除影子状态，所有方法改为快照模式 |
| `AneiangYarpServiceCollectionExtensions.cs` | DI 注册替换 |
| `ConfigEntityMapper.cs` | 映射逻辑简化 |

### 不需要修改（~15 个文件）

所有通过 `IDynamicYarpConfigService` 接口或 `GetDynamicConfig()` 访问的调用方，因兼容属性保持不变：
`ConfigManagementController`、`ConfigPersistenceService`、`GatewayIdentityService`、`CircuitBreakerMiddleware`、`RateLimitMiddleware`、`RequestRetryMiddleware`、`DashboardEditablePolicy`、`DashboardRouteQueryService`、`DashboardClusterMapper`、`DefaultHealthCheckService`、`CircuitBreakerWarmupService`、`GatewayRegistryGrpcService`、`GatewayConfigController`、`HealthCheckController`、Benchmark mock。

---

## 九、实施步骤

```
Phase 1: 模型层重构（组合模式）
  ├── Step 1: 重构 DynamicRouteConfig.cs（加 Config 属性 + 兼容属性）
  ├── Step 2: 重构 DynamicClusterConfig.cs（同上）
  ├── Step 3: 更新 ConfigEntityMapper.cs（映射简化）
  └── Step 4: 编译验证

Phase 2: 配置提供器（消除影子状态）
  ├── Step 5: 创建 AneiangProxyConfig.cs
  ├── Step 6: 创建 AneiangProxyConfigProvider.cs
  ├── Step 7: 更新 AneiangYarpServiceCollectionExtensions.cs
  └── Step 8: 编译验证

Phase 3: 核心服务重构
  ├── Step 9:  重构 DynamicYarpConfigService 字段和构造函数
  ├── Step 10: 重构启动流程（LoadAndApplyConfig）
  ├── Step 11: 重构读方法（无锁）
  ├── Step 12: 重构写方法（快照模式 + 增量持久化）
  ├── Step 13: 消除 BuildRouteConfig/BuildClusterConfig/ApplyDynamicConfigToYarp/ValidateConsistency
  └── Step 14: 编译验证 + 逻辑审查

Phase 4: 清理
  ├── Step 15: 逐步将兼容属性调用迁移为 .Config.XXX
  └── Step 16: 删除兼容属性
```

---

## 十、收益总结

| 指标 | 当前 | 重构后 |
|---|---|---|
| 内存状态份数 | 2（影子 + YARP） | 1（快照） |
| 读操作锁 | 有（SemaphoreSlim） | 无（volatile 读） |
| 字段覆盖 | 部分（依赖 ConfigJson 兜底） | 完整（直接持有原生类型） |
| YARP 升级兼容 | 需手动加字段 | 自动 |
| 启动步骤 | 5 步（含一致性校验） | 2 步 |
| Delete 持久化 | O(N) 全量 | O(1) 增量 |
| 心跳锁竞争 | 有 | 无 |
| 双写同步代码 | ~200 行 | 0 |

---

## 十一、风险与缓解

| 风险 | 等级 | 缓解措施 |
|---|---|---|
| 快照替换时 YARP 短暂双重读取 | 低 | 不可变快照，读旧或读新都正确 |
| `GetDynamicConfig()` 返回值变化 | 低 | 返回新 `GatewayDynamicConfig` 对象，调用方均只读 |
| 心跳不再触发快照更新 | 低 | 心跳不影响路由，独立存储更合理 |
| `ConfigurationReloadToken` 使用错误 | 中 | 参照 YARP 官方 `InMemoryConfigProvider` 源码实现 |
| 持久化失败导致内存/SQLite 不一致 | 中 | 与当前行为一致（不回滚），增加日志告警 |
| 静态配置合并逻辑复杂 | 中 | 保留现有 `MarkStaticConfig` 核心逻辑，简化为快照构建 |
| 旧数据无 ConfigJson | 低 | 映射层 fallback 从分散字段重建 |

---

## 十二、改造后可删除的冗余代码

### 12.1 DynamicYarpConfigService.cs（删除最多）

#### 影子状态相关 — 完全删除

| 方法/字段 | 删除原因 |
|---|---|
| `GatewayDynamicConfig? _dynamicConfig` (字段) | 影子状态消失，由快照取代 |
| `InMemoryConfigProvider _configProvider` (字段) | 替换为 `AneiangProxyConfigProvider` |
| `HashSet<string> _staticRouteIds` (字段) | 静态配置合并逻辑简化 |
| `HashSet<string> _staticClusterIds` (字段) | 同上 |
| `EnsureDynamicConfigInitialized()` | 无影子状态可初始化 |
| `ValidateConsistency()` | 单一数据源，无双状态可校验 |
| `ApplyDynamicConfigToYarp()` | 不再全量推送，由 `ApplySnapshot` 取代 |

> `long _configVersion` 字段保留，仍用于快照版本号。

#### Build / Patch / 序列化方法 — 完全删除

| 方法 | 删除原因 |
|---|---|
| `BuildRouteConfig(DynamicRouteConfig)` | 直接用 `dynRoute.Config`，无需从 ConfigJson 重建 |
| `BuildClusterConfig(DynamicClusterConfig)` | 直接用 `dynCluster.Config` |
| `PatchRouteConfigJson(...)` | `with` 表达式直接改原生类型，无需 patch JSON 字符串 |
| `PatchClusterConfigJson(...)` | 同上 |
| `TrySerializeRoute(RouteConfig)` | 序列化移到映射层 `ToEntity` |
| `TrySerializeCluster(ClusterConfig)` | 同上 |

#### 持久化方法 — 大幅简化

| 方法 | 处理方式 |
|---|---|
| `PersistConfigToRepositoryAsync()` 全量 fallback 路径 | 删除全量加载+删除+重存逻辑，改为各方法内联增量调用 |
| `PersistConfigToRepositorySync()` | 启动流程改为 `PersistAllToRepository`，可删除或简化 |
| `TryPersistIncrementalAsync()` | 增量逻辑下沉到各写方法，此分发器可删除 |
| `LoadConfigFromRepository()` | 逻辑并入 `LoadAndApplyConfig()`，可删除 |

#### 需评估保留的方法

| 方法 | 评估 |
|---|---|
| `MarkStaticConfig()` | **简化保留** — 核心合并逻辑仍需，删除 `RemoveAll` + 影子同步部分 |
| `MergeRouteMetadata()` | **保留** — 推送 YARP 时合并 `Config.Metadata` + `PolicyMetadata` |
| `SanitizeClusters()` | **保留** — 清理空 destination 仍有价值，调用点减少 |
| `NormalizeTransforms()` | **保留** — transform 规范化逻辑独立 |
| `ResolveClusterUid()` | **评估** — 若 route 不再存 ClusterUid 则可删，否则保留 |
| `BuildClusterHealthCheck()` | **保留** — Model→YARP HealthCheck 转换仍需 |

#### 内联消除的重复样板

每个写方法 `finally` 块里的重复模式可全部删除：

```csharp
finally
{
    if (saveNeeded)
    {
        Interlocked.Increment(ref _configVersion);
        _dynamicConfig!.Version = _configVersion;
        await PersistConfigToRepositoryAsync(...);
    }
    _semaphore.Release();
}
```

改造后版本号递增、快照应用、增量持久化进入主流程，`saveNeeded` 标志位模式全部删除。

### 12.2 模型层

| 文件 | 删除内容 |
|---|---|
| `DynamicRouteConfig.cs` | `RouteId`/`ClusterId`/`MatchPath`/`Order`/`Transforms`/`Metadata`/`ConfigJson`/`ClusterUid`/`DisplayName`/`RouteKey` 独立存储字段（Phase 4 删除兼容属性） |
| `DynamicClusterConfig.cs` | `ClusterId`/`Destinations`/`LoadBalancingPolicy`/`HealthCheck`/`ConfigJson`/`ClusterKey`/`DisplayName` 独立存储字段（同上） |

> Phase 1-3 期间这些字段以「兼容属性」形式存在（getter/setter 代理到 `Config`），Phase 4 待调用方全部迁移为 `.Config.XXX` 后才真正删除。

### 12.3 映射层

| 文件 | 删除内容 |
|---|---|
| `ConfigEntityMapper.cs` | `ToRouteConfig`/`ToClusterConfig` 中分散字段逐个映射 → 改为优先 `Deserialize(ConfigJson)`，分散字段仅作 fallback |
| `EntityMapper.cs`（Dashboard） | 与 `ConfigEntityMapper.cs` 重复定义的 `ToEntity`/`ToRouteConfig`/`ToClusterConfig`，建议合并消除两套映射 |

### 12.4 DI 注册 AneiangYarpServiceCollectionExtensions.cs

| 删除 | 替换为 |
|---|---|
| `services.AddSingleton<InMemoryConfigProvider>(...)` | `AneiangProxyConfigProvider` 注册 |
| `services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<InMemoryConfigProvider>())` | 指向 `AneiangProxyConfigProvider` |

### 12.5 代码量预估

| 文件 | 可删除行数（估） |
|---|---|
| `DynamicYarpConfigService.cs` | ~400-500 行（Build/Patch/Apply/Validate/全量持久化/saveNeeded 样板） |
| `DynamicRouteConfig.cs` | ~30 行（Phase 4） |
| `DynamicClusterConfig.cs` | ~30 行（Phase 4） |
| `ConfigEntityMapper.cs` | ~20 行 |
| `EntityMapper.cs`（Dashboard） | ~60 行（若合并重复映射） |
| **合计** | **~550-650 行** |

### 12.6 删除时的注意事项

1. **`ConfigJson` 数据库列不要删** — `RouteEntity.ConfigJson` / `ClusterEntity.ConfigJson` 列必须保留，它是新架构持久化原生配置的载体。删的只是模型层 `DynamicRouteConfig.ConfigJson` 这个**计算属性**。

2. **兼容属性分两阶段** — Phase 1-3 保留兼容属性确保 ~15 个调用方不报错；Phase 4 用 grep 确认无 `.RouteId`/`.MatchPath` 等旧式访问后再删。

3. **`ResolveClusterUid` 依赖链** — `RequestRetryMiddleware`、`RateLimitMiddleware`、`CircuitBreakerMiddleware` 通过 `GetDynamicConfig()` 读 `ClusterUid`/`RouteUid`，删字段前需确认这些中间件的 UID 来源。

