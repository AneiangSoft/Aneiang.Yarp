# Aneiang.Yarp Route/Cluster 插件化架构设计方案

## 1. 设计目标

Aneiang.Yarp 重构为一个极小的 YARP Core，以及一组围绕 Route 和 Cluster 按需装配的能力插件。

必须满足以下目标：

1. Route、Cluster 是唯一配置中心。
2. 所有非核心能力插件化。
3. 插件未启用时不加载程序集、不注册 DI、不插入中间件、不启动后台任务。
4. 删除系统功能配置页面。
5. 优先复用 YARP 原生能力。
6. 自定义能力使用统一插件模型。
7. 页面只展示当前已启用插件提供的配置。
8. 每个请求只计算一次有效插件配置。
9. 插件安装、启用、绑定和运行状态相互分离。
10. Route/Cluster 配置动态生效，插件启停通过网关平滑重载实现。

核心原则：

> 系统只管理 YARP 的 Route、Cluster 和 Destination；所有增强能力都是可选插件，插件配置直接附着在 Route 或 Cluster 上，不再存在独立的系统功能配置。

---

## 2. 核心领域模型

系统只保留三个核心代理对象：

```text
Route
Cluster
Destination
```

### 2.1 Route

Route 负责：

- 请求匹配。
- 选择 Cluster。
- 请求级策略。
- Route 作用域插件。

典型能力：

- Match。
- Transforms。
- Authorization。
- Timeout。
- RateLimit。
- WAF。
- Retry。
- Cache。
- CORS。
- Compression。
- ProxyLog。

### 2.2 Cluster

Cluster 负责：

- 后端目标集合。
- 负载均衡。
- 后端通信参数。
- Cluster 作用域策略。
- Cluster 作用域插件。

典型能力：

- Destinations。
- LoadBalancing。
- SessionAffinity。
- HealthCheck。
- HttpClient。
- HttpRequest。
- CircuitBreaker。
- ServiceDiscovery。
- Cluster Metrics。

### 2.3 Destination

Destination 主要保存：

- 后端地址。
- 健康状态。
- 熔断状态。
- 服务发现元数据。
- 运行指标。

Destination 主要是 Cluster 的运行成员，不承担大量策略编辑。

---

## 3. 删除系统配置

删除 Dashboard 中以下独立功能配置入口：

- 系统 WAF 配置。
- 系统重试配置。
- 系统熔断配置。
- 系统限流配置。
- 系统缓存配置。
- 系统压缩配置。
- 系统日志策略配置。
- 模块设置页面。
- 全局功能开关页面。

用户以后只需要确认两个问题：

```text
插件是否启用？
当前 Route 或 Cluster 是否绑定了该插件？
```

### 3.1 最小宿主配置

删除系统功能配置不等于宿主没有启动配置。`appsettings.json` 只保留：

- Dashboard 监听地址。
- Proxy 监听地址。
- 数据库连接。
- Dashboard 身份认证。
- 插件目录。
- 已启用插件清单。
- 网关节点标识。

示例：

```json
{
  "Gateway": {
    "NodeId": "gateway-01",
    "ManagementUrl": "http://0.0.0.0:5200",
    "ProxyUrl": "http://0.0.0.0:5202"
  },
  "Storage": {
    "ConnectionString": "..."
  },
  "Plugins": {
    "Directory": "plugins",
    "Enabled": [
      "aneiang.waf",
      "aneiang.retry",
      "aneiang.proxy-log"
    ]
  }
}
```

这些配置属于 Bootstrap，不进入日常功能设置页面。

---

## 4. 插件生命周期

### 4.1 插件状态

插件分为四种状态：

```text
未安装
已安装但未启用
已启用但未绑定
已启用且已绑定
```

#### 未安装

- 插件文件不存在。
- 页面不显示。
- 不能配置。

#### 已安装但未启用

- 只读取轻量 `plugin.json`。
- 不执行 `Assembly.Load`。
- 不注册 DI。
- 不插入中间件。
- 不启动 Hosted Service、Timer、Channel 或网络连接。
- Route/Cluster 页面不显示该能力。

#### 已启用但未绑定

- 插件程序集已经加载。
- 允许在 Route/Cluster 页面绑定。
- 不为未绑定目标创建运行状态。
- 没有绑定时不进入 Route 执行计划。
- 非必要后台任务不得启动。

#### 已启用且已绑定

- 注册插件服务。
- 编译 Route/Cluster 插件配置。
- 生成对应执行器。
- 按目标作用域进入请求执行计划。

### 4.2 插件启停

Route/Cluster 插件参数和绑定关系支持动态更新，无需重启。

以下操作触发网关工作进程平滑重载：

- 启用插件。
- 禁用插件。
- 升级插件。
- 删除插件。

推荐流程：

```text
保存新的插件启用清单
    ↓
启动新网关工作进程
    ↓
加载新的插件集合
    ↓
新进程通过健康检查
    ↓
切换流量
    ↓
停止旧进程
```

单机环境可以退化为自动重启。

这样可以保证禁用插件时：

- 程序集不加载。
- DI 无注册。
- 无 Middleware。
- 无 Hosted Service。
- 无 Timer、Channel 和缓存。
- 请求管线无插件判断开销。

---

## 5. 插件目录和清单

推荐插件目录：

```text
plugins/
└── Aneiang.Plugin.Waf/
    ├── plugin.json
    ├── Aneiang.Plugin.Waf.dll
    └── assets/
```

`plugin.json` 示例：

```json
{
  "id": "aneiang.waf",
  "name": "WAF",
  "version": "2.0.0",
  "entryAssembly": "Aneiang.Plugin.Waf.dll",
  "entryType": "Aneiang.Plugin.Waf.WafPlugin",
  "scopes": ["Route"],
  "capabilities": ["RequestMiddleware", "Dashboard"],
  "order": 100,
  "resources": {
    "requestMiddleware": true,
    "backgroundServices": false,
    "database": false,
    "networkConnections": false
  }
}
```

插件未启用时，只读取 `plugin.json`，不加载 DLL。

---

## 6. 插件契约

建议将插件声明、服务注册、配置编译和运行执行分开。

```csharp
public interface IGatewayPlugin
{
    PluginDescriptor Descriptor { get; }

    void ConfigureServices(
        IServiceCollection services,
        PluginStartupContext context);

    void ConfigurePipeline(
        IPluginPipelineBuilder pipeline);

    void ConfigureDashboard(
        IPluginDashboardBuilder dashboard);
}
```

Route 插件编译器：

```csharp
public interface IRoutePluginCompiler
{
    string PluginId { get; }

    CompiledRoutePlugin Compile(
        RoutePluginBinding binding,
        RouteConfig route);
}
```

Cluster 插件编译器：

```csharp
public interface IClusterPluginCompiler
{
    string PluginId { get; }

    CompiledClusterPlugin Compile(
        ClusterPluginBinding binding,
        ClusterConfig cluster);
}
```

中间件不得在每个请求中访问数据库、解析 JSON 或重新读取 Options。插件绑定在配置发布阶段编译为不可变运行时配置。

---

## 7. 统一 Route 配置模型

示例：

```json
{
  "uid": "route-uid",
  "routeId": "payment-api",
  "clusterUid": "payment-cluster-uid",
  "order": 0,
  "match": {
    "path": "/api/payment/{**catch-all}",
    "methods": ["GET", "POST"]
  },
  "transforms": [],
  "native": {
    "authorizationPolicy": null,
    "timeout": "00:00:30",
    "rateLimiterPolicy": null
  },
  "plugins": {
    "aneiang.waf": {
      "policy": "strict",
      "inspectRequestBody": true
    },
    "aneiang.retry": {
      "maxAttempts": 2,
      "allowedMethods": ["GET"],
      "statusCodes": [502, 503, 504]
    }
  }
}
```

Route 基础模型负责 YARP 原生字段；插件绑定负责扩展能力。

---

## 8. 统一 Cluster 配置模型

示例：

```json
{
  "uid": "cluster-uid",
  "clusterId": "payment-cluster",
  "native": {
    "loadBalancingPolicy": "PowerOfTwoChoices",
    "sessionAffinity": null,
    "healthCheck": {
      "active": {
        "enabled": true,
        "path": "/health"
      },
      "passive": {
        "enabled": true
      }
    },
    "httpClient": {},
    "httpRequest": {}
  },
  "destinations": {},
  "plugins": {
    "aneiang.circuit-breaker": {
      "failureRatio": 0.5,
      "minimumThroughput": 20,
      "samplingDuration": "00:00:30",
      "breakDuration": "00:00:20"
    },
    "aneiang.service-discovery": {
      "provider": "consul",
      "serviceName": "payment-service"
    }
  }
}
```

---

## 9. 插件绑定持久化

复杂插件配置不再存储为大量 YARP Metadata 字符串。

### 9.1 插件定义

```text
gateway_plugins
- PluginId
- Version
- Installed
- Enabled
- Manifest
- UpdatedAt
```

### 9.2 Route 插件绑定

```text
route_plugin_bindings
- RouteUid
- PluginId
- ConfigJson
- SchemaVersion
- Enabled
- UpdatedAt
```

唯一索引：

```text
RouteUid + PluginId
```

同一路由对同一种插件只允许一个绑定。

### 9.3 Cluster 插件绑定

```text
cluster_plugin_bindings
- ClusterUid
- PluginId
- ConfigJson
- SchemaVersion
- Enabled
- UpdatedAt
```

唯一索引：

```text
ClusterUid + PluginId
```

### 9.4 插件运行状态

```text
plugin_runtime_states
- PluginId
- TargetType
- TargetUid
- NodeId
- StateJson
- UpdatedAt
```

运行状态包括：

- 熔断状态。
- 限流统计摘要。
- WAF 拦截数。
- 重试次数。
- 服务发现状态。

配置数据和运行状态必须分开。

---

## 10. YARP 原生能力适配原则

插件分为两类。

### 10.1 Native Adapter Plugin

YARP 或 ASP.NET Core 已经原生支持，插件只负责：

- 提供配置 Schema 和管理页面。
- 校验参数。
- 将插件配置编译为 YARP 原生字段。
- 不插入额外请求中间件。

包括：

- Route Transforms。
- Route Authorization。
- Route Timeout。
- Route RateLimiter。
- Route CORS。
- Cluster LoadBalancing。
- Cluster HealthCheck。
- Cluster SessionAffinity。
- Cluster HttpClient。
- Cluster HttpRequest。

这类插件运行时没有额外请求开销，由 YARP 或 ASP.NET Core 直接执行。

### 10.2 Runtime Plugin

YARP 没有原生实现，需要实际运行组件：

- WAF。
- Retry。
- CircuitBreaker。
- ProxyLog。
- Cache。
- ServiceDiscovery。
- 自定义认证。
- 动态或分布式限流。

---

## 11. 推荐插件清单

### 11.1 Core，不可禁用

Gateway Core 只负责：

- YARP Host。
- Route、Cluster、Destination。
- 动态配置发布。
- 配置持久化。
- 插件宿主。
- 最小管理 API。
- 健康检查。

Core 不包含：

- WAF。
- 请求/响应正文日志。
- Retry。
- CircuitBreaker。
- RateLimit。
- Cache。
- ServiceDiscovery。

### 11.2 Route Native Adapter

- `Route.Transforms`。
- `Route.Authorization`。
- `Route.Timeout`。
- `Route.RateLimiter`。
- `Route.Cors`。

### 11.3 Cluster Native Adapter

- `Cluster.LoadBalancing`。
- `Cluster.HealthCheck`。
- `Cluster.SessionAffinity`。
- `Cluster.HttpClient`。
- `Cluster.HttpRequest`。

### 11.4 Route Runtime Plugin

#### WAF

作用域：Route。

职责：

- IP、Header、Path、Query 规则。
- 请求正文检测。
- SQL 注入、XSS 检测。
- 请求大小限制。
- 拦截和审计。

#### Retry

作用域：Route。

职责：

- 可重试方法和状态码。
- 异常重试。
- 退避和 Jitter。
- 请求正文缓存。
- 单次尝试超时。
- 重新选择 Destination。

#### Response Cache

作用域：Route。

#### Compression

作用域：Route。

#### Proxy Log

作用域：Route。

配置：

- 元数据日志。
- 请求正文。
- 响应正文。
- 最大捕获长度。
- 脱敏规则。
- 采样率。

未启用时：

- 不创建 Channel。
- 不启动 SQLite Writer。
- 不替换响应流。
- 不读取请求正文。
- 不启动持久化 Hosted Service。

### 11.5 Cluster Runtime Plugin

#### Circuit Breaker

配置作用域：Cluster。

状态作用域：

```text
Cluster + Destination
```

职责：

- Closed、Open、HalfOpen 状态机。
- 失败比例。
- 最小吞吐。
- 采样窗口。
- BreakDuration。
- HalfOpen 试探。
- Destination 隔离。

Route 不直接配置熔断参数。

#### Service Discovery

作用域：Cluster。

可提供：

- Consul。
- Nacos。
- Kubernetes。
- Eureka。
- 静态发现。

服务发现插件最终只更新 `Cluster.Destinations`。

#### Cluster Metrics

作用域：Cluster。

提供：

- 请求量。
- 延迟。
- 错误率。
- Destination 健康状态。

---

## 12. 限流设计

### 12.1 标准限流插件

使用 ASP.NET Core/YARP 原生能力，支持：

- Fixed Window。
- Sliding Window。
- Token Bucket。
- Concurrency。

配置绑定到 Route：

```json
{
  "algorithm": "FixedWindow",
  "partitionBy": "ClientIp",
  "permitLimit": 100,
  "window": "00:01:00",
  "queueLimit": 0
}
```

插件动态创建 Route 对应策略，并将名称编译到：

```text
RouteConfig.RateLimiterPolicy
```

### 12.2 分布式限流插件

独立为：

```text
Aneiang.Plugin.DistributedRateLimit.Redis
```

只有多节点环境需要时才启用。未启用时：

- 不加载 Redis 客户端。
- 不建立 Redis 连接。
- 不启动刷新任务。
- 不占用连接池。

---

## 13. 健康检查、熔断和重试的关系

### 健康检查

由 YARP 原生 Cluster HealthCheck 负责：

```text
该 Destination 当前是否可用？
```

### 熔断

由 CircuitBreaker 插件负责：

```text
最近的真实业务请求是否持续失败？
```

### 重试

由 Retry 插件负责：

```text
当前请求失败后是否允许再次尝试？
```

执行关系：

```text
Retry 选择一个可用 Destination
    ↓
跳过健康检查失败的 Destination
    ↓
跳过熔断 Open 的 Destination
    ↓
发送请求
    ↓
将结果反馈给熔断器
    ↓
符合重试条件时重新选择另一个 Destination
```

请求级候选集合：

```text
Healthy Destinations
- Open Circuit Destinations
- Already Attempted Destinations
= Retry Candidates
```

禁止通过临时修改 YARP 共享健康状态实现跨目标重试。

---

## 14. 请求执行管线

推荐顺序：

```text
1. ForwardedHeaders
2. Route Matching
3. Authorization
4. CORS
5. WAF
6. RateLimit
7. Cache Lookup
8. Retry Coordinator
9. Circuit Breaker Destination Filter
10. YARP Forwarder
11. Response Capture
12. Cache Write
13. Metrics/Log
```

配置发布时，为每条 Route 编译专属执行计划：

```text
RouteExecutionPlan
- RouteUid
- PreProxyHandlers[]
- ForwardHandler
- PostProxyHandlers[]
- ClusterExecutionPlan
```

普通 Route 示例：

```text
Authorization
→ YARP
```

支付 Route 示例：

```text
Authorization
→ WAF
→ RateLimit
→ Retry
→ CircuitBreaker
→ YARP
→ ProxyLog
```

未绑定插件不会进入该 Route 的执行数组。

---

## 15. 配置快照

建立统一 `GatewaySnapshot`：

```text
GatewaySnapshot
├── YarpRoutes
├── YarpClusters
├── RouteExecutionPlans
├── ClusterExecutionPlans
└── Version
```

发布流程：

```text
读取 Route/Cluster
    ↓
读取已启用插件
    ↓
读取插件绑定
    ↓
插件校验配置
    ↓
编译 YARP RouteConfig/ClusterConfig
    ↓
编译插件执行计划
    ↓
整体校验
    ↓
原子发布 GatewaySnapshot
```

发布失败时：

- 不发布半成品。
- 保留上一版本。
- 返回明确错误。
- 标记失败插件和目标对象。

---

## 16. Dashboard 页面规划

最终导航收敛为：

```text
概览
路由
集群
插件
流量
审计
```

菜单由插件动态贡献：

- ProxyLog 未启用，不显示日志菜单。
- WAF 未启用，不显示 WAF 页面或 Route WAF 卡片。
- CircuitBreaker 未启用，不显示 Cluster 熔断配置和状态。

### 16.1 Route 页面

基础配置始终显示：

- Route ID。
- Cluster。
- Match Path。
- Hosts。
- Methods。
- Headers。
- Query Parameters。
- Order。

能力配置只展示已启用且支持 Route 的插件：

```text
已绑定能力
├── WAF
├── 限流
└── 重试

可添加能力
├── 超时
├── 缓存
├── CORS
└── 响应压缩
```

用户点击“添加能力”后，直接绑定到当前 Route。

### 16.2 Cluster 页面

基础配置：

- Cluster ID。
- Destinations。
- 负载均衡。

能力配置：

```text
已绑定能力
├── 健康检查
├── 熔断
└── 服务发现

可添加能力
├── Session Affinity
├── HttpClient
└── Cluster Metrics
```

### 16.3 插件页面

只负责插件生命周期：

- 安装。
- 启用。
- 禁用。
- 升级。
- 卸载。
- 查看版本和依赖。
- 查看绑定的 Route/Cluster。
- 查看资源使用和插件健康状态。

插件页面不再保存全局 WAF、Retry、CircuitBreaker、RateLimit 参数。

插件确实依赖 Redis、Consul 等基础设施时，只允许在插件页面配置资源连接，不配置请求策略。

---

## 17. 策略预设

第一版删除独立策略中心和复杂继承。

可以提供“保存为预设”：

```text
当前 Route 的 WAF 配置
→ 保存为预设“互联网严格防护”
```

预设只在创建配置时复制内容，不参与运行时继承。

最终运行配置始终只有一份：

```text
Route 插件配置
或
Cluster 插件配置
```

后续只有在大量 Route 确实需要同步策略时，再增加共享策略绑定。

---

## 18. 插件配置 Schema

插件提供 JSON Schema 或等价定义：

```json
{
  "type": "object",
  "properties": {
    "maxAttempts": {
      "type": "integer",
      "minimum": 1,
      "maximum": 5,
      "title": "最大尝试次数"
    },
    "allowedMethods": {
      "type": "array",
      "items": {
        "enum": ["GET", "HEAD", "PUT", "DELETE", "POST"]
      }
    }
  }
}
```

Dashboard 根据 Schema 自动生成表单。

插件可提供自定义前端组件，但基础插件优先使用 Schema 表单，以实现：

- 新插件无需修改 Dashboard 主程序。
- 统一配置验证。
- 字段说明随插件发布。
- 插件升级可迁移 Schema。
- 删除大量硬编码设置页面。

---

## 19. 配置版本和插件升级

每份绑定保存：

```text
PluginId
PluginVersion
SchemaVersion
ConfigJson
```

插件升级提供：

```csharp
IPluginConfigurationMigrator
```

升级流程：

```text
读取旧配置
→ 执行迁移
→ 校验
→ 生成新快照
→ 发布
```

失败时保持旧插件和旧配置继续运行。

---

## 20. 插件资源规范

插件必须禁止以下行为：

- 静态构造器启动 Timer。
- 注册后立即创建线程。
- 未绑定时建立外部连接。
- 未使用时启动 Channel 消费者。
- 在每个请求中访问数据库。
- 在请求中反复解析 JSON 配置。

插件清单应声明：

```json
{
  "resources": {
    "requestMiddleware": true,
    "backgroundServices": false,
    "database": false,
    "networkConnections": false
  }
}
```

插件页面展示这些资源特征。

---

## 21. 现有功能归类

| 当前功能 | 新归属 | 作用域 |
|---|---|---|
| Route/Cluster CRUD | Core | Route/Cluster |
| DynamicConfig | Core | GatewaySnapshot |
| WAF | Runtime Plugin | Route |
| Retry | Runtime Plugin | Route |
| CircuitBreaker | Runtime Plugin | Cluster + Destination 状态 |
| RateLimit | Native Adapter/Distributed Plugin | Route |
| HealthCheck | Native Adapter | Cluster |
| LoadBalancing | Native Adapter | Cluster |
| SessionAffinity | Native Adapter | Cluster |
| Transforms | Native Adapter | Route |
| Timeout | Native Adapter | Route |
| Authorization | Native Adapter | Route |
| CORS | Native Adapter | Route |
| Cache | Runtime Plugin | Route |
| Compression | Runtime Plugin/ASP.NET Adapter | Route |
| ServiceDiscovery | Runtime Plugin | Cluster |
| ProxyLog | Runtime Plugin | Route |
| Traffic Metrics | Observability Plugin | Route/Cluster |
| Dashboard Auth | Bootstrap/Core | Management Plane |
| Health Endpoints | Core | Gateway Node |

---

## 22. 推荐项目结构

```text
Aneiang.Yarp.Core
├── Route/Cluster 模型
├── 动态配置
├── GatewaySnapshot
├── Plugin Abstractions
└── YARP Host

Aneiang.Yarp.Dashboard
├── Route/Cluster 管理
├── Plugin 生命周期管理
├── Schema Form Renderer
└── 动态导航

Aneiang.Yarp.Plugin.Native.Timeout
Aneiang.Yarp.Plugin.Native.RateLimit
Aneiang.Yarp.Plugin.Native.HealthCheck
Aneiang.Yarp.Plugin.Native.LoadBalancing
Aneiang.Yarp.Plugin.Waf
Aneiang.Yarp.Plugin.Retry
Aneiang.Yarp.Plugin.CircuitBreaker
Aneiang.Yarp.Plugin.ProxyLog
Aneiang.Yarp.Plugin.Cache
Aneiang.Yarp.Plugin.ServiceDiscovery.Consul
Aneiang.Yarp.Plugin.RateLimit.Redis
```

第一方插件和第三方插件使用同一套契约。

---

## 23. 迁移计划

### 第一阶段：收敛配置入口

- 删除系统配置导航。
- Route 页面增加插件能力区。
- Cluster 页面增加插件能力区。
- 插件页面只保留生命周期管理。
- 保留旧数据结构，但停止新增旧式配置。

### 第二阶段：建立新插件绑定模型

- 新增 `route_plugin_bindings`。
- 新增 `cluster_plugin_bindings`。
- 新增插件 Schema 和配置校验。
- 建立 `GatewaySnapshot`。
- 建立 Route/Cluster 执行计划。

### 第三阶段：迁移原生能力

按顺序迁移：

1. Timeout。
2. LoadBalancing。
3. HealthCheck。
4. Transforms。
5. Authorization。
6. RateLimit。

这些能力优先映射到 YARP 原生配置，不写重复中间件。

### 第四阶段：重写增强插件

按顺序迁移：

1. ProxyLog。
2. WAF。
3. Retry。
4. CircuitBreaker。
5. Cache。
6. ServiceDiscovery。

每迁移一个模块，删除对应的：

- 系统 Options。
- Settings Controller。
- 独立设置页面。
- 旧 Route Metadata。
- 旧 Middleware 注册。
- 旧 Hosted Service 注册。

### 第五阶段：真正按需加载

- 使用 `plugin.json` 进行无程序集扫描。
- 仅加载启用插件。
- 仅注册启用插件的 DI。
- 仅构建启用插件的管线。
- 插件启停触发平滑重载。
- 增加插件资源占用统计。

### 第六阶段：删除兼容层

全部迁移完成后删除：

- 综合策略模型。
- 全局模块配置。
- 旧插件总开关。
- 重复限流系统。
- Route/Cluster 扩展中的大量布尔字段。
- 字符串化插件 Metadata。
- 各模块独立设置服务。

---

## 24. 最终用户体验

创建支付网关时，用户只需要执行以下操作。

### 创建 Cluster

```text
Cluster：payment-cluster
Destinations：
- https://payment-01/
- https://payment-02/

添加能力：
- PowerOfTwoChoices
- 主动健康检查
- 熔断
```

### 创建 Route

```text
Route：payment-route
Path：/api/payment/{**catch-all}
Cluster：payment-cluster

添加能力：
- WAF
- 每 IP 限流
- 请求超时
- 日志
```

支付创建接口不能重试，则不绑定 Retry 插件。

查询接口允许重试，则创建独立查询 Route 并绑定 Retry 插件。

用户不再需要寻找：

- 系统重试是否开启。
- 全局 WAF 是否开启。
- 模块设置是否覆盖。
- 插件设置是否生效。
- 策略是否正确绑定。

---

## 25. 最终架构原则

```text
YARP 原生能实现
→ 插件只负责配置适配，不重复实现

YARP 原生不能实现
→ Runtime Plugin 实现

插件未启用
→ 不加载、不注册、不进入管线、不启动后台任务

插件启用但目标未绑定
→ 不参与该目标的请求执行

配置位置
→ 只在 Route 或 Cluster 页面
```

最终架构：

```text
Aneiang.Yarp Core
├── Route
├── Cluster
├── Destination
├── Plugin Host
├── GatewaySnapshot
└── Dashboard

Optional Plugins
├── Native Adapter Plugins
└── Runtime Plugins
```

该方案用于彻底解决系统配置、模块设置、插件开关、策略绑定和 Route/Cluster 参数并存导致的配置复杂性，同时保留 YARP 原生模型、动态管理能力和第三方插件扩展能力。
