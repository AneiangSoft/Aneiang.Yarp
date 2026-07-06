# Aneiang.Yarp.Dashboard 内存优化完整方案（适配当前项目）

> 编写时间：2026-07-06  
> 修订时间：2026-07-07（补充遗漏点：采样Bug、Channel阻塞风险、3个无清理ConcurrentDict、Retry MemoryStream、热路径字符串分配等）
> 问题背景：100线程×100循环压力测试下，内存占用升至800-900MB且持续增长  
> 核心约束：日志功能不能关闭（这是真实生产吞吐），需要保留请求日志  
> 适配版本：基于 Aneiang.Yarp.Dashboard 当前模块化架构重新调整

---

## 一、问题根因分析

当前架构中所有日志数据驻留在内存环形缓冲区中，且多个模块均从该缓冲区做全量实时聚合：

| 内存热点 | 每请求分配量 | 100并发持续影响 |
|---------|------------|---------------|
| YarpRequestCaptureMiddleware (2 LogEntry/请求) | 2个 LogEntry + 2个 Dictionary + 大字符串 | 累积增长 |
| TeeResponseCaptureStream (响应体) | 1个 MemoryStream (预分配64KB) | LOH 碎片 |
| DownstreamCaptureTransform (byte[]) | 1个 byte[] → string (条件触发) | 双重缓冲 |
| ProxyLogStore 环形缓冲区 (512条默认，NextPowerOf2对齐500→512) | 常驻 15-25MB | 不释放旧字符串 |
| DashboardApiController.GetStats() | 每5秒取2000条做全量聚合 | 短时大量分配 |
| OperationsController.GetTrafficData() | 取5000条做全量聚合 | 更严重 |
| OperationsController.GetTopIssues() | 取5000条做全量聚合 | 同上 |
| TrafficBroadcastService | 每2秒取500条做聚合 | 持续CPU+内存尖峰 |
| CooldownManager._cooldowns | ConcurrentDictionary无自动清理 | 无限增长风险 |
| WafEventStore._events | ConcurrentQueue保留1000条WafSecurityEvent | 高频攻击场景快速填充 |
| RateLimitMiddleware.Limiters | ConcurrentDictionary上限10000，清理策略粗暴 | 大量IP+路径组合内存占用 |
| WafMiddleware.ReadBodyAsync | `new char[maxScanBytes]` 每请求最大100KB | 高并发分配压力 |
| ⚠️ **ProxyRequest 在 ShouldLog 前写入（逻辑Bug）** | 采样模式下 ProxyEntry 不被过滤，仍占内存 | 采样失效 + "孤儿"条目累积 |
| RequestRetryMiddleware (每次重试2个 MemoryStream) | 重试场景 2×N MemoryStream分配 | 5次重试 = 10次分配热点 |
| TraceId.ToString() + Guid.NewGuid().ToString() (每请求) | 2个临时字符串分配/请求 | 100并发数百次/秒 |
| UpstreamPath 重复拼接 (Path+QueryString×2) | 2个冗余字符串/请求 | 每请求2次相同拼接 |
| Headers Dictionary 内部哈希桶 (2个/请求) | 2个 Dictionary + buckets+entries 数组 | Dictionary 开销远超内容 |
| AneiangProxyConfigProvider._heartbeats | ConcurrentDictionary只增不删 | 集群删除后心跳条目残留 |
| IpMatcher.WildcardRegexCache | ConcurrentDictionary无清理 | 配置变更后旧Regex残留 |
| ChannelSender._channelLocks | ConcurrentDictionary无清理 | SemaphoreSlim永不释放 |
| NotifySubscribers 热路径 lock+ToArray() | 每条日志 lock + 数组复制 | 热路径不应有锁 |

**本质问题**：高吞吐下 LogEntry 及关联大字符串全部驻留内存，GC 回收滞后。同时 Stats/Operations/TrafficBroadcast 多处从同一环形缓冲区做全量聚合计算，每次请求都产生大量临时 List/Dictionary 对象。此外，采样逻辑存在 Bug 导致过滤失效，多个 ConcurrentDictionary 缺乏清理机制存在长期泄漏风险，请求热路径上存在过多字符串拼接和 lock 操作。

**与参考方案的区别**：当前项目不存在 YarpEventSourceListener（只有 ProxyRequest + ProxyResponse），LogSanitizer 已使用 static JsonSerializerOptions 和 cached HashSet，ProxyLogStore 已使用 lock-free 环形缓冲区 + 位掩码优化。

---

## 二、优化方案（5个阶段）

### 阶段0：P0 关键 Bug 修复（必须在所有其他步骤之前）

**目标**：修复采样逻辑失效 + Channel 阻塞风险，否则后续优化无法生效

| # | 文件 | 动作 | 详细说明 | 优先级 |
|---|------|------|---------|--------|
| 0.1 | `Infrastructure/Yarp/YarpRequestCaptureMiddleware.cs` ProxyRequest添加逻辑 | **修复采样Bug** | 当前 ProxyRequest 条目在 `ShouldLog` 检查之前就被写入缓冲区（约第151行），导致采样模式下被丢弃的请求仍占用内存且产生无 ProxyResponse 的"孤儿"条目。修复：将 ProxyRequest 的添加移到 `ShouldLog` 检查之后，或让 ShouldLog 同时决定是否记录 ProxyRequest。**这是严重逻辑错误**，导致采样和 MinLogLevel 过滤完全失效 | **P0** |
| 0.2 | `Modules/ProxyLog/Services/ProxyLogStore.cs` Channel 创建 | **Channel 满时策略改为 DropNewest** | 当前方案 `Channel.CreateBounded<LogEntry>(1000)` 默认 `BoundedChannelFullMode.Wait` — 如果 SQLite 写入跟不上，中间件线程会被阻塞在 `WriteAsync` 上，直接影响代理请求延迟。日志是运维辅助数据，不应阻塞核心代理功能。改为 `BoundedChannelFullMode.DropNewest`（丢弃最新日志而非阻塞），配合丢失计数器供前端提示 | **P0** |
| 0.3 | `Modules/ProxyLog/Services/ProxyLogStore.cs` _count 字段 | **移除或修正 _count 死代码** | 当前 `_count` 在缓冲区满后永远停留在 `_bufferLength`，成为死代码。GetRecent() 直接用 `_head` 计算。移除 `_count` 字段避免误导消费方，或修正其逻辑使其准确反映当前条目数 | P2 |

---

### 阶段1：日志持久化 + 冷热分离 + 内存缓冲区缩减

**目标**：减少 90% 日志常驻内存，支持历史查询

#### 1.1 架构变更

```
当前架构：
  请求 → 中间件 → LogEntry → ProxyLogStore(内存,512条) → UI轮询(5秒)
                                                   → Stats取2000条
                                                   → Operations取5000条
                                                   → TrafficBroadcast取500条

新架构：
  请求 → 中间件 → LogEntry → Channel<LogEntry>(生产者)
                                    ↓
                              AsyncLogPersistenceService(后台消费者,批量写入SQLite)
                                    ↓
                              proxy_logs_meta + proxy_logs_body (SQLite,冷热分离)
                                    ↓
                              ProxyLogStore(内存,50条,仅供实时推送)
                                    ↓
                              Stats/Operations → SQLite meta 表聚合（不再取内存缓冲区）
                              TrafficBroadcast → SQLite meta 表或内存实时统计累加器
```

#### 1.2 SQLite 冷热分离表设计

**轻量元数据表**（用于列表查询、筛选、统计，每行约200-500B）：

```sql
CREATE TABLE IF NOT EXISTS proxy_logs_meta (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp       TEXT NOT NULL,        -- ISO 8601
    EventType       TEXT NOT NULL,        -- 'ProxyRequest' or 'ProxyResponse'
    Level           TEXT NOT NULL,
    RouteId         TEXT,
    ClusterId       TEXT,
    Method          TEXT,
    UpstreamPath    TEXT NOT NULL,
    StatusCode      INTEGER,
    ElapsedMs       REAL,
    TraceId         TEXT,
    HasRequestBody  INTEGER DEFAULT 0,
    HasResponseBody INTEGER DEFAULT 0,
    DownstreamUrl   TEXT
);
CREATE INDEX IF NOT EXISTS idx_meta_time ON proxy_logs_meta(Timestamp);
CREATE INDEX IF NOT EXISTS idx_meta_route ON proxy_logs_meta(RouteId);
CREATE INDEX IF NOT EXISTS idx_meta_cluster ON proxy_logs_meta(ClusterId);
CREATE INDEX IF NOT EXISTS idx_meta_status ON proxy_logs_meta(StatusCode);
CREATE INDEX IF NOT EXISTS idx_meta_trace ON proxy_logs_meta(TraceId);
```

**大字段详情表**（仅在用户点击单条日志查看详情时查询）：

```sql
CREATE TABLE IF NOT EXISTS proxy_logs_body (
    MetaId          INTEGER PRIMARY KEY REFERENCES proxy_logs_meta(Id) ON DELETE CASCADE,
    Message         TEXT,
    RequestBody     TEXT,
    ResponseBody    TEXT,
    RequestHeaders  TEXT,    -- JSON string
    ResponseHeaders TEXT,    -- JSON string
    DownstreamBody  TEXT,
    Exception       TEXT
);
```

#### 1.3 数据量估算与保留策略

| 维度 | 值 |
|------|---|
| 7天高负载(1000req/min) meta 表 | ~14MB (约200行/min × 7天) |
| 7天高负载 body 表 | ~700MB |
| meta 表保留 | **7天**（自动清理） |
| body 表保留 | **3天**（自动清理，meta CASCADE删除） |
| 清理后 meta 表大小 | 永远 ≤14MB |
| 清理后 body 表大小 | ≤300MB |
| 列表查询速度 | 索引查询毫秒级（只扫轻量 meta 表） |
| 详情查询速度 | 单行查询毫秒级（按 MetaId 查 body） |

#### 1.4 改动清单

| # | 文件 | 动作 | 详细说明 |
|---|------|------|---------|
| 1.1 | **新增** `Modules/ProxyLog/Services/IProxyLogPersistenceService.cs` | 接口 | `Task WriteAsync(LogEntry entry)` — 标记 LogEntry 需要持久化 |
| 1.2 | **新增** `Modules/ProxyLog/Services/AsyncLogPersistenceService.cs` | 后台服务 | `IHostedService`，从 `Channel<LogEntry>` 消费，每100条或每500ms批量写入 SQLite。含自动清理逻辑（每小时检查，删除超期 meta+body 行） |
| 1.3 | **新增** `Modules/ProxyLog/Services/SqliteProxyLogWriter.cs` | 写入器 | 在 `Aneiang.Yarp.Storage.Sqlite` 中增加两张表建表逻辑，实现 `WriteBatchAsync(List<LogEntry>)` 批量写入。使用 WAL 模式 + `BEGIN TRANSACTION` 批量事务。**注意：当前项目使用 `Aneiang.Yarp.Storage.Sqlite` 而非 SharedSqliteStore，需通过 `Aneiang.Yarp.Extensions.AneiangYarpServiceCollectionExtensions.AddAneiangStorage()` 注册的 `IProxyConfigRepository` 等接口操作 SQLite** |
| 1.4 | **改造** `Modules/ProxyLog/Services/ProxyLogStore.cs` | 内存缓冲区缩减 | `Add()` 方法同时写入内存缓冲区（容量从512缩至50，NextPowerOf2对齐后实际为64条）和 `Channel<LogEntry>`（容量1000，**BoundedChannelFullMode.DropNewest**，不阻塞中间件线程）。被覆盖位置的旧 LogEntry 大字段置 null。新增 `_droppedCount` 丢失计数器（Channel 满时自增），通过 API `/api/logs/stats` 返回丢失数供前端提示 |
| 1.5 | **改造** `Infrastructure/DashboardOptions.cs` | 配置 | `LogBufferCapacity` 默认值从500改为50（NextPowerOf2对齐后实际缓冲区容量为64条）。新增配置项：`LogPersistenceEnabled`（bool，默认true）、`LogMetaRetentionDays`（int，默认7）、`LogBodyRetentionDays`（int，默认3） |
| 1.6 | **改造** `Modules/ProxyLog/Services/DashboardLogQueryService.cs` | 查询改造 | `GetLogs()` 仅从内存缓冲区获取最近50条（用于实时展示）；新增 `GetHistoryLogs(int page, int pageSize, ProxyLogSearchRequest filter)` 从 SQLite meta 表分页查询；新增 `GetLogDetail(long metaId)` 从 body 表查单条详情 |
| 1.7 | **改造** `Abstractions/IDashboardLogQueryService.cs`（如有）或 `Modules/ProxyLog/Services/IDashboardLogQueryService.cs` | 接口扩展 | 新增 `GetHistoryLogs()` 和 `GetLogDetail()` 方法 |
| 1.8 | **新增** `Modules/ProxyLog/Models/ProxyLogSearchRequest.cs` | 搜索模型 | 字段：Page、PageSize、RouteId?、ClusterId?、Level?、StatusCodeMin?、StatusCodeMax?、StartTime?、EndTime?、Keyword?、EventType? |
| 1.9 | **新增** `Modules/ProxyLog/Models/ProxyLogSearchResult.cs` | 结果模型 | 字段：Items(List\<ProxyLogMetaItem\>)、TotalCount、Page、PageSize、HasMore |
| 1.10 | **新增** `Modules/ProxyLog/Models/ProxyLogMetaItem.cs` | 轻量展示模型 | 字段：Id、Timestamp、EventType、Level、RouteId、ClusterId、Method、UpstreamPath、StatusCode、ElapsedMs、TraceId、HasRequestBody、HasResponseBody、DownstreamUrl |
| 1.11 | **改造** `Modules/Dashboard/Controllers/DashboardApiController.cs` | API扩展 | 新增 `[HttpGet("api/logs/history")]` 历史分页查询接口；新增 `[HttpGet("api/logs/detail/{id}")]` 单条详情接口；`GetStats()` 改为从 SQLite meta 表聚合统计（不再从内存缓冲区取2000条）；新增 `[HttpGet("api/logs/stats")]` 返回 Channel 丢失计数器值 |
| 1.12 | **改造** `Modules/Operations/Controllers/OperationsController.cs` | API改造 | `GetTrafficData()` 改为从 SQLite meta 表聚合（不再取5000条）；`GetTopIssues()` 同样改为 SQL 聚合；`GetAlertSummary()` 中的 `_logQuery.GetLogs(1000)` 改为 SQL 查询近5分钟数据 |
| 1.13 | **改造** `Infrastructure/Realtime/TrafficBroadcastService.cs` | 改为SQL聚合或统计累加器 | `_logStore.GetRecent(500)` 改为从 SQLite meta 表查近60秒数据，或改用 `LockFreeStatistics` 实时累加器（项目已有此基础设施） |
| 1.14 | **改造** `Extensions/DashboardServiceCollectionExtensions.cs` | DI注册 | 注册 `AsyncLogPersistenceService` 为 `IHostedService`；注册 `SqliteProxyLogWriter` 为 Singleton；注册 `IProxyLogPersistenceService` |
| 1.15 | **改造** `Modules/ProxyLog/Services/ProxyLogStoreExtensions.cs` | 移除 EvictedCount | 内存缓冲区缩小后，evicted 概念不再重要。简化 `NotifySubscribers` 逻辑 |
| 1.16 | **改造** `Modules/Waf/Models/WafSecurityEvent.cs` WafEventStore | WAF事件持久化 | 将 `WafEventStore` 纳入持久化改造，新增 SQLite `waf_events_meta` 表存储 WAF 安全事件元数据（ClientIp、EventType、RuleName、RequestUri、MatchedValue 等）。内存缓冲区从1000条缩减到50条（NextPowerOf2后为64条），仅在内存中保留最近事件用于实时展示。`WafSecurityEvent` 大字段（MatchedValue、RequestUri）在被 dequeued 前置 null 释放引用 |
| 1.17 | **新增** `Modules/Waf/Services/WafEventPersistenceService.cs` | WAF事件后台持久化 | 与 `AsyncLogPersistenceService` 类似架构，从 Channel 消费 WAF 事件批量写入 SQLite |

#### 1.5 Channel 批量写入策略

```csharp
// AsyncLogPersistenceService 核心逻辑（伪代码）
private readonly Channel<LogEntry> _channel = Channel.CreateBounded<LogEntry>(
    new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.DropNewest  // ⚠️ 不阻塞中间件线程，丢弃最新日志而非 Wait
    });
private int _droppedCount = 0;  // 丢失计数器，供前端提示

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    var batch = new List<LogEntry>(100);
    while (!stoppingToken.IsCancellationRequested)
    {
        // 等待第一条或超时500ms
        var first = await _channel.Reader.ReadAsync(stoppingToken);
        batch.Add(first);
        
        // 尝试批量读取更多（最多100条）
        while (batch.Count < 100 && _channel.Reader.TryRead(out var entry))
            batch.Add(entry);
        
        // 批量写入 SQLite
        await _writer.WriteBatchAsync(batch);
        
        // 每小时执行一次清理 + WAL checkpoint
        if (DateTime.UtcNow - _lastCleanup > TimeSpan.FromHours(1))
        {
            await _writer.CleanupAsync(_options.LogMetaRetentionDays, _options.LogBodyRetentionDays);
            await _writer.CheckpointAsync();  // PRAGMA wal_checkpoint(TRUNCATE)
            _lastCleanup = DateTime.UtcNow;
        }
        
        batch.Clear();
    }
}
```

#### 1.6 Stats API 改造

当前 `DashboardApiController.GetStats()` 从内存缓冲区取2000条做聚合，非常低效。改为从 SQLite meta 表聚合：

```sql
-- 替代内存缓冲区遍历的 SQL 查询
SELECT 
    COUNT(*) as total,
    SUM(CASE WHEN StatusCode BETWEEN 200 AND 399 THEN 1 ELSE 0 END) as success,
    SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) as error,
    AVG(ElapsedMs) as avgLatency
FROM proxy_logs_meta 
WHERE Timestamp >= datetime('now', '-5 minutes');

-- P50/P90/P99 需要排序后取百分位（SQLite 不直接支持 percentile 函数）
SELECT ElapsedMs FROM proxy_logs_meta 
WHERE Timestamp >= datetime('now', '-5 minutes') AND EventType = 'ProxyResponse' 
ORDER BY ElapsedMs LIMIT 1 OFFSET (COUNT * 0.5);
```

#### 1.7 Operations API 改造

当前 `OperationsController` 三个接口都取 5000 条做全量聚合，是最严重的内存热点：

```sql
-- GetTrafficData: 按时间分桶统计
SELECT 
    strftime('%Y-%m-%d %H:%M', Timestamp) as time_bucket,
    COUNT(*) as qps,
    SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) as errors
FROM proxy_logs_meta
WHERE Timestamp >= @startTime AND EventType = 'ProxyResponse'
GROUP BY time_bucket
ORDER BY time_bucket;

-- GetTopIssues: 错误路由排行
SELECT 
    RouteId,
    COUNT(*) as totalCount,
    SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) as errorCount
FROM proxy_logs_meta
WHERE Timestamp >= @startTime AND EventType = 'ProxyResponse'
GROUP BY RouteId
ORDER BY errorCount DESC
LIMIT @count;

-- GetAlertSummary: 近5分钟500错误数
SELECT COUNT(*) FROM proxy_logs_meta
WHERE Timestamp >= datetime('now', '-5 minutes') AND StatusCode >= 500 AND EventType = 'ProxyResponse';
```

#### 1.8 TrafficBroadcastService 改造

当前每2秒取500条做聚合。改为两种策略（可选其一）：

**策略A：SQLite聚合**（简单，适合中等流量）
```csharp
// 每2秒从 SQLite meta 表查近60秒数据
var metrics = await _sqlWriter.GetRecentRouteMetricsAsync(TimeSpan.FromSeconds(60));
```

**策略B：LockFreeStatistics 实时累加器**（项目已有基础设施，适合高流量）
```csharp
// 利用现有的 LockFreeStatistics / ConcurrentIntDictionary 基础设施
// 在 YarpRequestCaptureMiddleware.Add() 时同时累加到统计器
// TrafficBroadcastService 每2秒从统计器读取，零分配
// 项目已有此基础设施但未被主流程启用
```

---

### 阶段2：前端 UI 改造

**目标**：配合后端持久化，增加历史日志查询

#### 2.1 改动清单

| # | 文件 | 动作 | 详细说明 |
|---|------|------|---------|
| 2.1 | `wwwroot/js/modules/dashboard-logs.js` | **增加历史日志标签页** | 在日志页面顶部增加两个标签页：「实时日志」（当前功能）和「历史日志」（新功能）。实时日志仍从内存API `/api/logs` 获取；历史日志从 `/api/logs/history` 分页获取 |
| 2.2 | `wwwroot/js/modules/dashboard-logs.js` | **增加历史日志搜索** | 历史日志标签页包含：时间范围选择器（起始/结束时间）、RouteId下拉、ClusterId下拉、状态码范围、级别选择、关键词搜索、分页控件（上一页/下一页/页码） |
| 2.3 | `wwwroot/js/modules/dashboard-logs.js` | **增加详情按需加载** | 实时日志和历史日志的展开详情，如果发现 `hasRequestBody=true` 或 `hasResponseBody=true` 但 `requestBody/responseBody` 为 null（因为内存只存50条元数据），则额外调用 `/api/logs/detail/{id}` 获取大字段 |
| 2.4 | `wwwroot/js/core/dashboard-api.js` | **增加 API endpoints** | 新增：`getLogHistory: (params) => DashboardApi.get('/api/logs/history', params)` 和 `getLogDetail: (id) => DashboardApi.get(`/api/logs/detail/${id}`)` |
| 2.5 | `wwwroot/js/modules/dashboard-logs.js` | **调整轮询逻辑** | 实时日志轮询间隔保持5秒不变，但每次只取最近50条（内存缓冲区容量）。历史日志不轮询，按需查询 |
| 2.6 | `Infrastructure/I18n/zh-CN/waf.json` 或专用日志i18n文件 | **增加 i18n key** | 新增：`index.log.historyTab`(历史日志)、`index.log.realtimeTab`(实时日志)、`index.log.searchHistory`(搜索历史)、`index.log.timeRange`(时间范围)、`index.log.viewDetail`(查看详情)、`index.log.noDetail`(暂无详情)、`index.log.pagination`(分页)、`index.log.prevPage`(上一页)、`index.log.nextPage`(下一页)、`index.log.totalRecords`(共{0}条) |
| 2.7 | `Infrastructure/I18n/en-US/` 对应文件 | **增加英文 i18n key** | 对应中文的英文翻译 |
| 2.8 | `wwwroot/js/modules/dashboard-logs.js` | **LogEntry 列表渲染改为轻量模式** | 实时日志从 `/api/logs` API 返回的数据中，内存缓冲区的50条 LogEntry 仍包含完整字段（用于实时展示）。历史日志列表只渲染 ProxyLogMetaItem 的轻量字段，点击展开时再调 `/api/logs/detail/{id}` 获取大字段 |

#### 2.2 UI 布局设计

```
┌─────────────────────────────────────────────────────────┐
│  [实时日志]  [历史日志]                   标签页切换      │
├─────────────────────────────────────────────────────────┤
│                                                         │
│ ── 实时日志标签页（当前功能，改动小）──                    │
│                                                         │
│  搜索框 │ 状态筛选 │ 条数选择 │ 监听 │ 刷新 │ 清空       │
│  ──────────────────────────────────────────────────      │
│  14:30  200  GET  [请求+响应]  /api/users  45ms  📋 ▶    │
│  14:29  404  POST [请求+响应]  /api/login   12ms  📋 ▶   │
│  ...                                                    │
│  显示 50 条 / 缓冲 64 条（对齐后）                       │
│                                                         │
│ ── 历史日志标签页（新功能）──                             │
│                                                         │
│  起始时间 │ 结束时间 │ Route ▼ │ Cluster ▼ │ 状态码范围   │
│  级别 ▼ │ 关键词搜索 │ 搜索按钮                    │
│  ──────────────────────────────────────────────────      │
│  #1  2026-07-05 14:30  200  GET  /api/users  45ms       │
│  #2  2026-07-05 14:29  404  POST /api/login  12ms       │
│  ...（点击展开 → 调 /api/logs/detail/{id} 获取完整信息） │
│  ──────────────────────────────────────────────────      │
│  ◀ 1 2 3 ... 10 ▶  共 2000 条                          │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

---

### 阶段3：减少单条 LogEntry 内存体积

**目标**：每条 LogEntry 内存体积减少 50%+

| # | 文件 | 动作 | 详细说明 |
|---|------|------|---------|
| 3.1 | `Infrastructure/Yarp/YarpRequestCaptureMiddleware.cs` TeeResponseCaptureStream 内部 | MemoryStream → RecyclableMemoryStream | 添加 NuGet `Microsoft.IO.RecyclableMemoryStream`。注册 `RecyclableMemoryStreamManager` 为 Singleton。TeeResponseCaptureStream 构造函数中 `new MemoryStream(...)` 改为 `manager.GetStream()`。用完后 `stream.Dispose()` 回收到底层 byte[] 池。**注意：TeeResponseCaptureStream 是中间件内部类** |
| 3.2 | `Infrastructure/Yarp/YarpRequestCaptureMiddleware.cs` 第165行 | ContentType为空时不读响应体 | `IsResponseBodyCaptureSafe` 中 ContentType 为空时返回 false（当前 `IsTextLikeContentType` 已对空ContentType返回false，但 `IsResponseBodyCaptureCandidate` 未检查ContentType） |
| 3.3 | `Modules/ProxyLog/Models/LogEntry.cs` | 大字段改为可 set | 在 `ProxyLogStore` 覆盖旧条目时需释放大字符串引用。将大字段从 `init` 改为 `set`：
```csharp
public string? RequestBody { get; set; }
public string? ResponseBody { get; set; }
public Dictionary<string, string>? RequestHeaders { get; set; }
public Dictionary<string, string>? ResponseHeaders { get; set; }
public string? DownstreamBody { get; set; }
public string? Exception { get; set; }
```
 |
| 3.4 | `Modules/ProxyLog/Services/ProxyLogStore.cs` Add() 方法 | 清空旧 LogEntry 大字段 | 在 `Add()` 中写入新条目前，检查被覆盖位置的旧 LogEntry，将其大字段设为 null 释放引用：
```csharp
public void Add(LogEntry entry)
{
    var index = Interlocked.Increment(ref _head) - 1;
    var slot = FastModulo(index);
    
    // 释放被覆盖旧条目的大字段引用
    var old = _buffer[slot];
    if (old != null)
    {
        old.RequestBody = null;
        old.ResponseBody = null;
        old.RequestHeaders = null;
        old.ResponseHeaders = null;
        old.DownstreamBody = null;
        old.Exception = null;
    }
    
    _buffer[slot] = entry;
    // ... rest of tracking logic
    ProxyLogStoreExtensions.NotifySubscribers(this, entry);
}
``` |
| 3.5 | `Infrastructure/Yarp/YarpRequestCaptureMiddleware.cs` | Headers 精简 — Dictionary→List或独立字段 | 仅记录关键 header（Content-Type、Content-Length、X-Request-Id），其余记为 `OtherHeadersCount: N`。当 `EnableProxyRequestBodyCapture=false` 且 `EnableProxyResponseBodyCapture=false` 时，可跳过 Headers 记录。**更激进方案**：将 `Dictionary<string, string>` 改为 `List<(string Key, string Value)>`（顺序查找对5-10个 header 足够快，内存开销远低于 Dictionary 的 buckets+entries 数组），或改为独立 string 字段（ContentType、ContentLength、XRequestId）+ OtherHeadersCount |
| 3.6 | `Modules/Waf/Middleware/WafMiddleware.cs` ReadBodyAsync | `new char[]` → ArrayPool<char> | 当前 `ReadBodyAsync` 使用 `new char[maxScanBytes]` 每次分配最大100KB `char[]`，高并发下是严重分配压力。改为 `ArrayPool<char>.Shared.Rent(maxScanBytes)` + 用完 `Return` |
| 3.7 | `Infrastructure/Yarp/YarpRequestCaptureMiddleware.cs` ReadBodyAsync | StreamReader.ReadToEndAsync → ArrayPool<char> 读取 | 当前 `ReadBodyAsync` 使用 `StreamReader.ReadToEndAsync()` 内部分配临时 `char[]` buffer，与 `ReadStreamAsync`（已使用 `ArrayPool<char>.Shared.Rent`）不对称。改为 `ArrayPool<char>.Shared.Rent` 方式读取
| 3.8 | `Infrastructure/Yarp/YarpRequestCaptureMiddleware.cs` ReadStreamAsync 截断路径 | 截断时避免双重字符串分配 | 当前截断路径 `new string(buffer, 0, read) + "\n... [TRUNCATED]"` 先从 ArrayPool buffer 创建一个 string，再拼接截断提示，总共分配2个字符串。改为：在 buffer 中追加截断标记后再 `new string()`，或使用 `string.Concat` 避免中间分配 |
| 3.9 | `Infrastructure/Yarp/YarpRequestCaptureMiddleware.cs` UpstreamPath | 消除重复拼接 | 当前 `context.Request.Path + context.Request.QueryString.Value` 在 ProxyRequest 和 ProxyResponse 两个 LogEntry 中各拼接一次（第159行和第238行），即每请求分配2个相同字符串。改为：拼接一次后存入局部变量，两个 LogEntry 共用同一个引用 |
| 3.10 | `Infrastructure/Yarp/YarpRequestCaptureMiddleware.cs` TraceId | TraceId.ToString() → ToHexString() 优化 | 当前每请求执行 `Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")`，`ActivityTraceId` 是结构体，`ToString()` 每次创建新字符串。100并发下每秒数百次分配。改为：使用 `ActivityTraceId.ToHexString()` 返回固定32字符 span（避免中间分配），或在 `ShouldLog` 通过后才计算 TraceId |
| 3.11 | `Infrastructure/Yarp/YarpRequestCaptureMiddleware.cs` Headers | Dictionary → List<(string,string)> 或独立字段 | 当前每请求创建 2 个 `Dictionary<string, string>(headers.Count)`，即使只有5-10个 header，Dictionary 内部的 `int[] buckets + Entry[] entries` 数组开销远超内容。方案3.5的"精简 Headers"可更激进：改为 `List<(string Key, string Value)>`（顺序查找对少量 header 足够快，内存开销小得多），或仅存关键 header 为独立 string 字段（ContentType、ContentLength、XRequestId）+ OtherHeadersCount |
| 3.12 | `Modules/ProxyLog/Models/LogEntry.cs` Message 字段 | Message 字段改为可选/延迟拼接 | Message 字段内容（如 `$"[Request] GET /api/users"`）完全可以由 EventType + Method + UpstreamPath + StatusCode 推导出来，是**冗余存储**。每条 LogEntry 的 Message 约 50-100 字节。改为：(1) 后端不存 Message 字段，改为 `init` 的 `string? Message = null`；(2) 前端根据 meta 字段拼接展示文本；(3) 仅在 SQLite meta 表不存 Message，body 表保留 |
| 3.13 | `Modules/RequestRetry/RequestRetryMiddleware.cs` MemoryStream | Retry 路径 MemoryStream → RecyclableMemoryStream | 当前每次重试迭代创建 2 个 MemoryStream（请求体 + 响应体，第118行和第123行），最多5次重试 = 10次 MemoryStream 分配。与 TeeResponseCaptureStream 一样改用 `RecyclableMemoryStreamManager.GetStream()` 池化复用。**注意：RequestRetryMiddleware 是独立中间件，需单独注入 RecyclableMemoryStreamManager** |
| 3.14 | `Modules/RequestRetry/RequestRetryMiddleware.cs` ReadRequestBodyAsync | ArrayPool + .ToArray() 矛盾修复 | 当前使用 `ArrayPool<byte>.Shared.Rent` 读取请求体，但最后 `buffer.AsSpan(0, read).ToArray()` 又做了一次复制分配，完全抵消了 ArrayPool 的节省。改为直接保留 ArrayPool buffer 引用 + 记录长度，或改用 RecyclableMemoryStream 避免中间复制 |

**关于 3.3（LogEntry 可变性）**：

当前 `LogEntry` 所有属性使用 `init`，无法在后续修改。需要将大字段改为可 set。核心字段（Timestamp、EventType、Level、Message、TraceId、RouteId、ClusterId、Method、UpstreamPath、StatusCode、ElapsedMs）保持 init 不变，仅大字段改为 set。

---

### 阶段4：次要内存优化（进一步减少分配）

| # | 文件 | 动作 | 说明 |
|---|------|------|------|
| 4.1 | `Modules/Notification/Services/CooldownManager.cs` | 定期清理过期条目 | `_cooldowns` ConcurrentDictionary 无自动清理，长时间运行会无限增长。添加后台清理逻辑：每5分钟清理所有 `now - value > maxCooldown` 的条目。或者改用 `ConcurrentDictionary<string, DateTime>` + 每次 TryAcquire 时顺便清理前N条过期条目 |
| 4.2 | `Infrastructure/Performance/LockFreeStatistics.cs` | 启用为主流程统计器 | 项目已有 `LockFreeStatistics` 基础设施（缓存行对齐、条纹锁字典、SIMD加速），但 DashboardApiController 和 OperationsController 未使用。改为：Stats API 从 LockFreeStatistics 读取预聚合数据 + SQLite聚合做补充，而非从内存缓冲区取2000/5000条 |
| 4.3 | `Infrastructure/Performance/ZeroAllocationLogPool.cs` | 启用 LogEntryStruct 路径 | 项目已有 `LogEntryStruct`（128字节结构体）、`LogEntryPool`（ArrayPool对象池）、`InlineStringBuffer`。当前主流程仍使用 class 版 LogEntry。如果日志仅用于统计而非详情展示，可以考虑热路径使用 LogEntryStruct，详情展示用 SQLite body 表 |
| 4.4 | `Modules/Dashboard/Controllers/DashboardApiController.cs` GetStats() | 添加 IMemoryCache 缓存 | 当前 Stats API 无缓存，每次请求都做全量聚合。改为聚合结果缓存10秒（项目已有 `IMemoryCache` 和 `DashboardCacheService`），减少重复计算 |
| 4.5 | `Modules/Operations/Controllers/OperationsController.cs` | 添加 IMemoryCache 缓存 | alert-summary 缓存10秒、traffic 缓存10秒、top-issues 缓存30秒 |
| 4.6 | `Infrastructure/Realtime/TrafficBroadcastService.cs` | 有客户端连接才聚合 | 当前无论是否有SignalR客户端连接都每2秒聚合。改为检查 `TrafficHub` 的连接数，无客户端时跳过聚合计算 |
| 4.7 | `Infrastructure/Performance/PooledStringBuilder.cs` | 扩大使用范围 | 项目已有 `PooledStringBuilder`，但 YarpRequestCaptureMiddleware 中 `ReadBodyAsync` 仍使用普通 StreamReader。改为 PooledStringBuilder + ArrayPool 读取 |
| 4.8 | `Modules/RateLimit/Middleware/RateLimitMiddleware.cs` TryCleanup() | RateLimiter 清理策略改进 | 当前 `Limiters` 上限10000太宽松，清理策略是直接删除前一半而非基于使用率的淘汰。改进：(1) 上限从10000降低到1000-2000；(2) 每个 `RateLimiter` 增加 `LastAccessedAt` 字段；(3) 清理策略改为淘汰最近5分钟无 `AcquireAsync` 调用的 limiter，而非粗暴删除前一半 |
| 4.9 | `Infrastructure/Yarp/YarpRequestCaptureMiddleware.cs` ShouldLog() | MinLogLevel缓存逻辑修复 | 当前 `_minLogLevel` 硬编码为0（Debug级别），导致 `DashboardOptions.MinLogLevel` 配置项无效。本应被过滤的低级别请求仍被记录，间接浪费内存。修复：`_minLogLevel` 应从 `DashboardOptions.MinLogLevel` 解析为 int 值（如 Information=1, Warning=2, Error=3），而非硬编码0 |
| 4.10 | `Infrastructure/Yarp/YarpRequestCaptureMiddleware.cs` LogEntry.Message | Message字段延迟拼接 | 当前每请求使用 `$` 字符串插值创建 `Message` 字段（如 `$"[Request] {context.Request.Method} {context.Request.Path}{context.Request.QueryString}"`），在100并发持续场景下累积分配。可改为仅在需要展示时才拼接，或使用 `PooledStringBuilder` 构建 |
| 4.11 | `Infrastructure/ConfigProvider/AneiangProxyConfigProvider.cs` _heartbeats | 清理过期集群心跳条目 | `_heartbeats` ConcurrentDictionary 只增不删，集群删除后对应心跳条目不会清除。在配置变更回调（`OnChange`）中清理不在新配置中的集群对应心跳条目，或每5分钟清理超过 `MaxHeartbeatAge` 的条目 |
| 4.12 | `Infrastructure/Waf/Services/IpMatcher.cs` WildcardRegexCache | 配置变更时重建 Regex 缓存 | `WildcardRegexCache` ConcurrentDictionary 中 Regex 对象一旦缓存永不释放，配置变更后旧 pattern 残留。在配置变更回调中调用 `WildcardRegexCache.Clear()` 重建缓存 |
| 4.13 | `Infrastructure/Realtime/ChannelSender.cs` _channelLocks | 频道删除时移除对应锁 | `_channelLocks` ConcurrentDictionary 中 SemaphoreSlim 对象永不释放，频道删除后锁仍存在。在频道清理逻辑中调用 `_channelLocks.TryRemove(key, out var lock)` + `lock.Dispose()` |
| 4.14 | `Modules/ProxyLog/Services/ProxyLogStoreExtensions.cs` NotifySubscribers | 热路径无锁化 | 当前 `NotifySubscribers` 在请求热路径上使用 `lock` + `subscribers.ToArray()`，每添加一条日志就复制 subscriber 列表。改为：使用 `Interlocked.CompareExchange` 或 `Volatile.Read` 读取 subscriber 引用（当前只有1个 subscriber），避免 lock。或将通知改为异步（通过 Channel 传递），不在热路径上直接通知 |
| 4.15 | `Modules/Waf/Models/WafSecurityEvent.cs` Id 字段 | Guid.NewGuid().ToString("N") → Guid 结构体 | 每个 WAF 安全事件调用 `Guid.NewGuid().ToString("N")` 创建32字符字符串。改为直接存为 `Guid` 结构体（16字节），ToString 仅在需要展示/持久化时调用；或使用递增 long Id 替代（SQLite 表已有 AUTOINCREMENT Id） |
| 4.16 | `Modules/Dashboard/Controllers/DashboardApiController.cs` BuildStatsResponse | 移除不必要的 DateTime List 分配 | 当前 `BuildStatsResponse` 中额外分配一个 `List<DateTime>(totalRequests)` 仅用于计算 `requestsPerMin`，但这个信息在聚合阶段已可获得。改为在聚合阶段直接按时间分桶计算 `requestsPerMin`，或在迁移到 SQLite 聚合后此问题自动解决 |
| 4.17 | AsyncLogPersistenceService 清理逻辑 | SQLite WAL checkpoint 策略 | 原方案提到使用 WAL 模式但没有定义 checkpoint 策略。SQLite WAL 文件如果不定期 checkpoint 会无限增长（尤其在高写入频率下）。在清理逻辑中每次清理后执行 `PRAGMA wal_checkpoint(TRUNCATE)`，或设置 `PRAGMA wal_autocheckpoint = 1000`（每1000页自动 checkpoint） |
| 4.18 | `Infrastructure/DashboardOptions.cs` LogBufferCapacity 运行时修改 | 标记为"仅启动时生效" | 方案7.6提出运行时修改配置，但 `ProxyLogStore._bufferLength` 是构造时确定的 readonly 字段，运行时修改 `LogBufferCapacity` 无法动态调整缓冲区大小。`LogBufferCapacity` 应标记为"仅启动时生效"，UI 上说明"修改缓冲区容量需重启服务"。其余配置（保留天数、捕获开关等）可以运行时修改 |

---

## 三、预期效果对比

| 维度 | 当前 | 优化后 |
|------|------|--------|
| 每请求 LogEntry 数量 | 2条（ProxyRequest + ProxyResponse） | 2条（不变，但大字段可释放） |
| 内存日志缓冲区容量 | 512条（默认，500→NextPowerOf2对齐） | 64条（实时，50→NextPowerOf2对齐）+ SQLite持久化 |
| 日志常驻内存 | 15-25MB | 1.5-2.5MB（64条对齐后） |
| 日志历史保留 | 环形缓冲区覆盖即丢失 | SQLite 永久保留（meta7天+body3天） |
| TeeResponseCaptureStream 分配 | 每请求 new MemoryStream → LOH碎片 | 池化复用，无LOH碎片 |
| RequestRetryMiddleware MemoryStream | 每次重试2个 MemoryStream | 池化复用，无LOH碎片 |
| Stats 统计 | 从内存取2000条遍历 | SQLite meta 表聚合 + 缓存10秒 |
| Operations 统计 | 从内存取5000条遍历 | SQLite meta 表聚合 + 缓存10-30秒 |
| TrafficBroadcast | 每2秒取500条聚合 | SQLite聚合或LockFreeStatistics |
| CooldownManager | 无限增长风险 | 定期清理过期条目 |
| 采样模式 | ShouldLog失效，ProxyRequest仍写入 | 修复后按需过滤，真正减少条目数 |
| Channel 满时 | 阻塞中间件线程（影响代理延迟） | DropNewest + 丢失计数器 |
| UpstreamPath 拼接 | 每请求2次冗余拼接 | 1次拼接，2条共用引用 |
| TraceId 分配 | 每请求 ToString() 2次 | ToHexString() 或延迟计算 |
| Headers 存储 | 2个 Dictionary（buckets+entries开销） | 精简字段或 List<(K,V)> |
| Message 字段 | 每条50-100B冗余存储 | 不存或延迟拼接 |
| 遗漏的 ConcurrentDict | _heartbeats/WildcardRegexCache/_channelLocks 无清理 | 配置变更/定期清理 |
| NotifySubscribers | 热路径 lock + ToArray() | 无锁读取或异步通知 |
| SQLite WAL | 无 checkpoint 策略 | 自动 checkpoint 每1000页 |
| 总体内存占用(10K请求压力) | 800-900MB | 预估 120-200MB |

---

## 四、执行路线图

| 步骤 | 改动范围 | 风险 | 预期收益 | 前置依赖 |
|------|---------|------|---------|----------|
| **Step -1** | 阶段0中 0.1: **修复采样Bug**（ProxyRequest移到ShouldLog之后） | 低-中 | 采样/过滤真正生效，减少无效条目 | **无**（P0，必须最先修复） |
| **Step -1b** | 阶段0中 0.2: **Channel满时策略改为DropNewest** | 低 | 防止日志写入阻塞代理请求线程 | 无（P0，与Step -1并行） |
| **Step 0** | 阶段3中 3.3-3.4: LogEntry可变性 + 旧条目大字段释放 | 低 | 为持久化释放大字段做准备 | **无**（必须最先完成，是阶段1的前提） |
| **Step 1** | 阶段3中 3.2 + 3.8-3.14: ContentType空值 + 截断双重分配 + UpstreamPath重复拼接 + TraceId优化 + Headers精简 + Message冗余 + RetryMemoryStream + ArrayPool矛盾 | 低 | 每请求减少约200-400B分配 | Step 0（3.3-3.4需先完成） |
| **Step 2** | 阶段4中 4.1-4.5 + 4.9 + 4.11-4.13 + 4.16: CooldownManager + 缓存 + 连接数检查 + MinLogLevel + _heartbeats + WildcardRegexCache + _channelLocks + Stats DateTime List | 低 | 内存泄漏修复 + CPU减少 + 配置生效 + 遗漏字典清理 | 无 |
| **Step 3** | 阶段1: 持久化架构改造（SQLite冷热分离 + Channel + 后台服务 + API改造） | 中 | 常驻内存减少90% | **Step 0 + Step -1b（3.3-3.4必须先完成 + Channel策略必须先定）** |
| **Step 4** | 阶段2: 前端历史日志标签页 + 详情按需加载 | 中 | 功能完善，配合持久化 | Step 3 |
| **Step 5** | 阶段3中 3.1 + 3.13: RecyclableMemoryStream（TeeStream + RetryMiddleware） | 低 | LOH碎片消除（覆盖2个MemoryStream热点） | 无 |
| **Step 6** | 阶段3中 3.5-3.7 + 3.11-3.12: Headers精简(Dictionary→List/独立字段) + WafMiddleware ArrayPool + ReadBodyAsync ArrayPool + Message冗余去除 | 低 | 减少每请求分配 + 去除冗余字段 | Step 0 |
| **Step 7** | 阶段4中 4.2-4.3: LockFreeStatistics启用 | 中 | 统计路径零分配 | Step 3（需先有SQLite聚合替代） |
| **Step 8** | 阶段4中 4.8: RateLimitMiddleware清理策略改进 | 低 | 防止Limiters字典无限增长 | 无 |
| **Step 9** | 阶段4中 4.14-4.15 + 3.10 + 0.3: NotifySubscribers无锁化 + WafSecurityEvent Guid优化 + TraceId优化 + _count死代码清理 | 低 | 热路径微优化 + 代码整洁 | Step 0 |
| **Step 10** | 阶段4中 4.17-4.18: WAL checkpoint策略 + LogBufferCapacity运行时限制标注 | 低 | SQLite WAL文件控制 + 配置语义准确 | Step 3（WAL与持久化同时部署） |

---

## 五、涉及文件完整清单

### 需新增的文件

- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/IProxyLogPersistenceService.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/AsyncLogPersistenceService.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/SqliteProxyLogWriter.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/Waf/Services/WafEventPersistenceService.cs` — WAF事件后台持久化服务
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Models/ProxyLogSearchRequest.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Models/ProxyLogSearchResult.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Models/ProxyLogMetaItem.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Models/LogPersistenceOptions.cs`（可选，也可合并到DashboardOptions）
- `src/Aneiang.Yarp.Dashboard/wwwroot/js/modules/dashboard-log-settings.js`（阶段6设置页面用）

### 需改造的文件

- `src/Aneiang.Yarp.Dashboard/Extensions/DashboardServiceCollectionExtensions.cs` — DI注册新服务（含 WafEventPersistenceService + RecyclableMemoryStreamManager）
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/ProxyLogStore.cs` — 缓冲区缩减(512→64) + Channel(DropNewest策略) + 旧条目释放 + _count死代码清理
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/ProxyLogStoreExtensions.cs` — 简化 NotifySubscribers（无锁化）
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/DashboardLogQueryService.cs` — SQLite查询方法
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/IDashboardLogQueryService.cs` — 接口扩展（或 Abstractions/ 下对应文件）
- `src/Aneiang.Yarp.Dashboard/Infrastructure/DashboardOptions.cs` — 新增配置项 + LogBufferCapacity默认值50(对齐后64) + LogBufferCapacity标注"仅启动时生效"
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Models/LogEntry.cs` — 大字段改为可 set + Message改为可选
- `src/Aneiang.Yarp.Dashboard/Modules/Dashboard/Controllers/DashboardApiController.cs` — 新增历史/详情API + Stats改为SQL聚合 + 移除DateTime List冗余分配
- `src/Aneiang.Yarp.Dashboard/Modules/Operations/Controllers/OperationsController.cs` — 改为SQL聚合 + 添加缓存
- `src/Aneiang.Yarp.Dashboard/Infrastructure/Realtime/TrafficBroadcastService.cs` — 改为SQL聚合或统计累加器
- `src/Aneiang.Yarp.Dashboard/Infrastructure/Yarp/YarpRequestCaptureMiddleware.cs` — **修复采样Bug(ProxyRequest移到ShouldLog后)** + RecyclableMemoryStream + Headers精简(Dictionary→List) + ReadBodyAsync ArrayPool + MinLogLevel修复 + UpstreamPath拼接复用 + TraceId优化 + 截断双重分配修复
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/DownstreamCaptureTransform.cs` — 条件捕获优化（已有基本条件，可进一步收紧）
- `src/Aneiang.Yarp.Dashboard/Modules/Notification/Services/CooldownManager.cs` — 定期清理过期条目
- `src/Aneiang.Yarp.Dashboard/Modules/Waf/Middleware/WafMiddleware.cs` — ReadBodyAsync `new char[]` → ArrayPool<char>
- `src/Aneiang.Yarp.Dashboard/Modules/Waf/Models/WafSecurityEvent.cs` — WafEventStore持久化 + 缓冲区缩减(1000→64) + Id改为Guid结构体
- `src/Aneiang.Yarp.Dashboard/Modules/RateLimit/Middleware/RateLimitMiddleware.cs` — Limiters上限降低 + 清理策略改进
- `src/Aneiang.Yarp.Dashboard/Modules/RequestRetry/RequestRetryMiddleware.cs` — MemoryStream→RecyclableMemoryStream + ArrayPool.ToArray()矛盾修复
- `src/Aneiang.Yarp.Dashboard/Infrastructure/ConfigProvider/AneiangProxyConfigProvider.cs` — _heartbeats过期清理
- `src/Aneiang.Yarp.Dashboard/Infrastructure/Waf/Services/IpMatcher.cs` — WildcardRegexCache配置变更时重建
- `src/Aneiang.Yarp.Dashboard/Infrastructure/Realtime/ChannelSender.cs` — _channelLocks频道删除时移除锁
- `src/Aneiang.Yarp.Dashboard/wwwroot/js/modules/dashboard-logs.js` — 历史标签页 + 详情按需加载
- `src/Aneiang.Yarp.Dashboard/wwwroot/js/core/dashboard-api.js` — 新增API endpoints
- `src/Aneiang.Yarp.Dashboard/wwwroot/js/core/dashboard-i18n.js` — i18n key扩展
- `src/Aneiang.Yarp.Dashboard/Infrastructure/I18n/zh-CN/` 对应json文件 — 中文i18n key
- `src/Aneiang.Yarp.Dashboard/Infrastructure/I18n/en-US/` 对应json文件 — 英文i18n key

### SQLite 相关改动

- 需在 `Aneiang.Yarp.Storage.Sqlite` 的 Schema 初始化中增加 `proxy_logs_meta`、`proxy_logs_body`、`waf_events_meta` 表的建表和索引创建
- 需在 `Aneiang.Yarp.Storage.Sqlite` 中增加 `proxy_log_settings` 表（阶段6设置页面用）
- 可能需要新增 `IProxyLogRepository` 接口在 `Aneiang.Yarp.Storage` 中

### 需新增的 NuGet 依赖

- `Microsoft.IO.RecyclableMemoryStream`（MIT License，微软官方库）

---

## 六、SQLite 查询性能保障

### 冷热分离确保查询快的原因

1. **列表查询只扫 meta 表**：meta 表每行约200-500B，7天数据约14MB，索引查询毫秒级
2. **大字段按需加载**：body 表只在用户点击单条日志详情时查单行，不影响列表性能
3. **自动清理确保数据库不膨胀**：meta 7天 + body 3天自动删除，meta 表永远≤14MB
4. **SQLite WAL 模式**：读写并发无冲突，批量写入不阻塞查询
5. **利用现有 `Aneiang.Yarp.Storage.Sqlite` 基础设施**：项目已有完整的 SQLite 存储层，只需扩展两张日志表

### Stats API 从 SQLite 聚合

```csharp
// 替代当前从内存缓冲区取2000条遍历的方式
public async Task<StatsResult> GetStatsFromSqliteAsync()
{
    // 近5分钟统计（高频查询，meta表索引命中）
    var fiveMinAgo = DateTime.UtcNow.AddMinutes(-5).ToString("o");
    
    // 使用 SQL 聚合，无需加载所有行到内存
    var sql = @"
        SELECT 
            COUNT(*) as TotalRequests,
            SUM(CASE WHEN StatusCode BETWEEN 200 AND 399 THEN 1 ELSE 0 END) as SuccessCount,
            SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) as ErrorCount,
            AVG(ElapsedMs) as AvgLatency
        FROM proxy_logs_meta 
        WHERE Timestamp >= @from AND EventType = 'ProxyResponse'";
    
    // P50/P90/P99 通过排序+OFFSET获取
    // ...
}
```

### Operations API 从 SQLite 聚合

```csharp
// 替代当前从内存缓冲区取5000条遍历的方式
// GetTrafficData: 分桶聚合
var trafficSql = @"
    SELECT 
        strftime('%Y-%m-%d %H:%M', Timestamp) as time_bucket,
        COUNT(*) as qps,
        SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) as errors
    FROM proxy_logs_meta
    WHERE Timestamp >= @startTime AND EventType = 'ProxyResponse'
    GROUP BY time_bucket
    ORDER BY time_bucket";

// GetTopIssues: 错误路由排行
var issuesSql = @"
    SELECT 
        RouteId,
        COUNT(*) as totalCount,
        SUM(CASE WHEN StatusCode >= 400 THEN 1 ELSE 0 END) as errorCount
    FROM proxy_logs_meta
    WHERE Timestamp >= @startTime AND EventType = 'ProxyResponse'
    GROUP BY RouteId
    ORDER BY errorCount DESC
    LIMIT @count";
```

---

## 七、设置页面 — 日志保留时长可配置

### 7.1 设计思路

当前系统设置页面（`Views/Dashboard/Settings.cshtml`）已有"导出配置"和"导入配置"等卡片。需要新增**「日志设置」**区域，让运维人员在 Dashboard UI 上直接调整日志保留策略、捕获开关等参数。

### 7.2 可配置项清单

| 配置项 | 类型 | 默认值 | 说明 | 当前项目是否已有 |
|--------|------|--------|------|----------------|
| `LogPersistenceEnabled` | bool | true | 日志持久化开关 | **新增** |
| `LogMetaRetentionDays` | int | 7 | 元数据保留天数 | **新增** |
| `LogBodyRetentionDays` | int | 3 | 大字段详情保留天数 | **新增** |
| `EnableProxyRequestBodyCapture` | bool | false | 请求体捕获开关 | **已有** |
| `EnableProxyResponseBodyCapture` | bool | false | 响应体捕获开关 | **已有** |
| `LogBufferCapacity` | int | 50 | 内存缓冲区容量（ProxyLogStore内部NextPowerOf2对齐，50→64条） | **已有(500→50)** |
| `LogMaxBodyLength` | int | 8192 | 单条日志最大body长度 | **已有** |
| `EnableLogSampling` | bool | false | 日志采样开关 | **已有** |
| `LogSamplingRate` | double | 1.0 | 采样率 | **已有** |
| `LogErrorsOnly` | bool | false | 仅记录错误请求 | **已有** |

### 7.3 UI 布局设计

在现有 `Settings.cshtml` 中增加一行卡片：

```
┌─────────────────────────────────────────────────────────┐
│  ⚙ 系统设置                                              │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  ┌─── 导出配置 ───┐  ┌─── 导入配置 ───┐                 │
│  │  (现有)        │  │  (现有)        │                 │
│  └────────────────┘  └────────────────┘                 │
│                                                         │
│  ┌─── 日志持久化设置 ──────────────────────────────┐    │
│  │  📊 日志设置                                      │    │
│  │                                                    │    │
│  │  ┌─ 持久化 ─────────────────────────────────┐     │    │
│  │  │ [✓] 启用日志持久化                         │     │    │
│  │  │ 元数据保留天数: [7] 天                      │     │    │
│  │  │ 详情保留天数:   [3] 天                      │     │    │
│  │  │ 内存缓冲容量:   [50] 条                     │     │    │
│  │  └───────────────────────────────────────────┘     │    │
│  │                                                    │    │
│  │  ┌─ 捕获 ────────────────────────────────────┐     │    │
│  │  │ [ ] 捕获请求体                             │     │    │
│  │  │ [ ] 捕获响应体                             │     │    │
│  │  │ 最大Body长度:  [8192] 字节                  │     │    │
│  │  └───────────────────────────────────────────┘     │    │
│  │                                                    │    │
│  │  ┌─ 采样 ────────────────────────────────────┐     │    │
│  │  │ [ ] 启用日志采样                           │     │    │
│  │  │ 采样率:        [1.0]                       │     │    │
│  │  │ [ ] 仅记录错误请求                          │     │    │
│  │  └───────────────────────────────────────────┘     │    │
│  │                                                    │    │
│  │  [💾 保存设置]  [↩ 重置默认]                      │    │
│  └────────────────────────────────────────────────────┘    │
│                                                         │
│  ⚡ 注意：修改保留天数后会立即清理超期数据，不可恢复       │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### 7.4 改动清单

| # | 文件 | 动作 | 详细说明 |
|---|------|------|---------|
| 7.1 | `Views/Dashboard/Settings.cshtml` | **增加日志设置卡片** | 在现有设置行下方新增一行，放置「日志设置」卡片。卡片内分三个区域：持久化、捕获、采样。每个配置项用 Bootstrap form controls 渲染 |
| 7.2 | **新增** `wwwroot/js/modules/dashboard-log-settings.js` | **设置页面交互逻辑** | (1) `loadSettings()` — 调用 GET `/api/logs/settings` 获取当前配置；(2) `saveSettings()` — 调用 PUT `/api/logs/settings` 提交修改；(3) `resetDefaults()` — 重置为默认值；(4) 校验逻辑：`LogBodyRetentionDays ≤ LogMetaRetentionDays`、`LogBufferCapacity ∈ [10,500]`、`LogSamplingRate ∈ [0.0,1.0]`；(5) 修改保留天数时弹出确认提示 |
| 7.3 | `Modules/Dashboard/Controllers/DashboardApiController.cs` | **增加设置 API** | 新增 `[HttpGet("api/logs/settings")]` 返回当前日志相关 DashboardOptions；新增 `[HttpPut("api/logs/settings")]` 接收修改 |
| 7.4 | `Infrastructure/DashboardOptions.cs` | **新增配置项** | 新增：`LogPersistenceEnabled`（bool，默认true）、`LogMetaRetentionDays`（int，默认7）、`LogBodyRetentionDays`（int，默认3） |
| 7.5 | **新增** `Modules/ProxyLog/Models/LogSettingsUpdateRequest.cs` | **设置更新模型** | 字段对应 7.2 中的可配置项，含验证规则 |
| 7.6 | **新增** `Modules/ProxyLog/Services/LogSettingsService.cs` | **运行时配置管理** | 负责将 UI 修改应用到运行时配置。核心挑战：`DashboardOptions` 通过 `IOptions<DashboardOptions>` 绑定，运行时不可直接修改。**方案**：使用 `IOptionsMonitor<DashboardOptions>` + SQLite `proxy_log_settings` 表，覆盖 appsettings.json 的默认值 |
| 7.7 | `Aneiang.Yarp.Storage.Sqlite` Schema | **增加设置表** | 新增 `proxy_log_settings` 表存储运行时日志配置 |
| 7.8 | `wwwroot/js/core/dashboard-api.js` | **增加 API endpoints** | 新增：`getLogSettings` 和 `updateLogSettings` |
| 7.9 | `Infrastructure/I18n/zh-CN/` 和 `en-US/` 对应json文件 | **增加 i18n key** | 新增中英文日志设置相关key |

### 7.5 运行时配置管理方案

**核心问题**：`DashboardOptions` 通过 `IOptions<DashboardOptions>` 绑定 `appsettings.json`，运行时不可修改。

**方案：SQLite 优先 + IOptionsMonitor**

```
配置读取优先级：
  1. SQLite proxy_log_settings 表（运行时 UI 修改的值）
  2. appsettings.json（初始默认值）

配置写入流程：
  UI 修改 → PUT /api/logs/settings → LogSettingsService 
    → 写入 SQLite proxy_log_settings 表
    → 更新内存中的运行时配置对象
    → 通知 AsyncLogPersistenceService 刷新保留策略
```

**SQLite settings 表设计**：

```sql
CREATE TABLE IF NOT EXISTS proxy_log_settings (
    Key   TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);
```

**启动流程**：

```
应用启动 → DI 构建 Aneiang.Storage.Sqlite → Schema 初始化(建表+索引)
  → LogSettingsService 从 SQLite proxy_log_settings 加载配置
    → 如果表为空（首次启动），从 IOptionsMonitor<DashboardOptions> 读默认值，写入 SQLite
  → AsyncLogPersistenceService 使用 LogSettingsService.GetCurrent() 的保留天数
  → ProxyLogStore 使用 LogSettingsService.GetCurrent() 的缓冲区容量
  → YarpRequestCaptureMiddleware 使用 LogSettingsService.GetCurrent() 的捕获开关
```

### 7.6 缩短保留天数时的清理策略

当用户在 UI 上缩短保留天数时：

1. **立即触发清理**：调用 `SqliteProxyLogWriter.CleanupAsync(newMetaDays, newBodyDays)`
2. **弹出确认提示**：UI 上提示 "缩短保留天数会立即删除超期数据，不可恢复"
3. **清理 SQL**：

```sql
-- 清理超期 meta（CASCADE 自动删 body）
DELETE FROM proxy_logs_meta 
WHERE Timestamp < datetime('now', '-{newMetaDays} days');

-- 清理超期 body（独立清理）
DELETE FROM proxy_logs_body 
WHERE MetaId NOT IN (SELECT Id FROM proxy_logs_meta)
   OR MetaId IN (
       SELECT MetaId FROM proxy_logs_body 
       INNER JOIN proxy_logs_meta ON proxy_logs_body.MetaId = proxy_logs_meta.Id
       WHERE proxy_logs_meta.Timestamp < datetime('now', '-{newBodyDays} days')
   );
```

---

## 八、风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| ⚠️ **采样Bug修复后过滤行为变化** | 原本被错误记录的低级别/采样丢弃请求不再出现在日志中 | 这是**修复Bug**而非功能变更。如需全量调试日志，可临时关闭采样/降低MinLogLevel |
| ⚠️ **Channel DropNewest导致高负载下日志丢失** | 部分日志在SQLite写入跟不上时被丢弃 | 日志是运维辅助数据，不应阻塞核心代理功能。配合丢失计数器 `_droppedCount`，前端可提示"部分日志因高负载未持久化"。正常吞吐下Channel容量1000足够，仅在极端场景触发 |
| SQLite 写入在高并发下瓶颈 | 后台批量写入延迟 | WAL模式 + 每100条批量事务 + Channel缓冲1000条兜底 |
| LogEntry 改为可 set 破坏不可变性设计 | 代码风格变更 | 仅大字段改为 set，核心字段保持 init。在 ProxyLogStore 内部控制，外部仍感知为只读 |
| Stats/Operations 改为SQL聚合后延迟 | 统计数据非实时 | IMemoryCache 缓存10秒 + 内存50条实时缓冲区仍可做补充。且生产环境10秒延迟完全可以接受 |
| SQLite 数据库文件损坏 | 历史日志丢失 | WAL模式 + 定期 checkpoint（每1000页自动 + 清理后 TRUNCATE）。Dashboard 主要是运维工具，日志丢失不影响代理转发功能 |
| 运行时修改配置不生效 | IOptions 不可运行时修改 | SQLite 优先 + LogSettingsService 内存缓存，绕过 IOptions 限制。**LogBufferCapacity标注"仅启动时生效"，需重启服务** |
| 缩短保留天数导致数据不可恢复 | 误操作风险 | UI 确认提示 + 建议先导出再缩短 |
| TrafficBroadcastService 改为SQL聚合 | 每2秒SQL查询可能影响SQLite | 优先使用 LockFreeStatistics 内存累加器，仅在无客户端连接时跳过 |
| CooldownManager 清理误删有效条目 | 通知漏发 | 清理阈值设为最大cooldown的2倍，确保安全 |
| RateLimitMiddleware 清理策略变更 | 限流器过早被淘汰 | 新的 LastAccessedAt 淘汰策略设置5分钟阈值，远大于正常请求间隔。同时1000-2000上限对大多数场景足够 |
| WafMiddleware ArrayPool 改造 | buffer 未正确 Return 导致内存泄漏 | 严格确保 `try/finally` 模式，在 `finally` 中 `ArrayPool<char>.Shared.Return(buffer)` |
| MinLogLevel 修复后过滤级别变化 | 部分请求不再被记录 | 这是预期行为——减少内存占用。如果需要调试级别日志，用户可在设置页面临时降低 MinLogLevel |
| RequestRetryMiddleware RecyclableMemoryStream | 用完后未 Dispose 导致池泄漏 | 确保所有 MemoryStream 使用 `using` 或 `try/finally` Dispose 回池 |
| _heartbeats 清理误删活跃集群 | 心跳信息丢失 | 仅清理不在当前配置中的集群心跳 + 超过 MaxHeartbeatAge 的条目 |
| WildcardRegexCache Clear 期间短暂无缓存 | WAF 规则匹配短暂走无缓存路径 | Clear 是瞬时操作，后续请求立即重建缓存 |
| _channelLocks 移除导致并发访问异常 | 频道清理与写入并发 | 移除前先 TryGetValue 检查是否存在活跃引用 |
| Headers Dictionary→List<(K,V)> | 列表顺序查找O(n)比Dictionary O(1)慢 | 单条日志通常只有5-10个 header，O(n)在n≤10时性能差异可忽略，内存开销远低于 Dictionary |
| NotifySubscribers 无锁化 | 极端情况下 subscriber 列表在读取时被修改 | 使用 `Volatile.Read` + 不可变数组模式（subscriber 变更时替换整个数组引用），避免 lock |
| SQLite WAL 文件增长 | 无 checkpoint 时 WAL 无限增长 | `PRAGMA wal_autocheckpoint = 1000` + 每次清理后 `PRAGMA wal_checkpoint(TRUNCATE)` |

---

## 九、涉及文件完整清单（更新版）

### 需新增的文件

- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/IProxyLogPersistenceService.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/AsyncLogPersistenceService.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/SqliteProxyLogWriter.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/LogSettingsService.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/Waf/Services/WafEventPersistenceService.cs` — WAF事件后台持久化
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Models/ProxyLogSearchRequest.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Models/ProxyLogSearchResult.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Models/ProxyLogMetaItem.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Models/LogSettingsUpdateRequest.cs`
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Models/LogPersistenceOptions.cs`
- `src/Aneiang.Yarp.Dashboard/wwwroot/js/modules/dashboard-log-settings.js`

### 需改造的文件

- `src/Aneiang.Yarp.Dashboard/Views/Dashboard/Settings.cshtml` — 增加日志设置卡片
- `src/Aneiang.Yarp.Dashboard/Extensions/DashboardServiceCollectionExtensions.cs` — DI 注册新服务（含 WafEventPersistenceService + RecyclableMemoryStreamManager）
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/ProxyLogStore.cs` — 缓冲区缩减(512→64) + Channel(DropNewest) + 旧条目释放 + _count死代码清理 + 丢失计数器
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/ProxyLogStoreExtensions.cs` — 简化 + 无锁化 NotifySubscribers
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/DashboardLogQueryService.cs` — SQLite 查询
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/IDashboardLogQueryService.cs` — 接口扩展
- `src/Aneiang.Yarp.Dashboard/Infrastructure/DashboardOptions.cs` — 新增配置项 + LogBufferCapacity 默认值50(对齐后64) + LogBufferCapacity标注"仅启动时生效"
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Models/LogEntry.cs` — 大字段改为可 set + Message改为可选
- `src/Aneiang.Yarp.Dashboard/Modules/Dashboard/Controllers/DashboardApiController.cs` — 新增设置/历史/详情 API + Stats改为SQL聚合 + 移除DateTime List冗余分配
- `src/Aneiang.Yarp.Dashboard/Modules/Operations/Controllers/OperationsController.cs` — 改为SQL聚合 + 缓存
- `src/Aneiang.Yarp.Dashboard/Infrastructure/Realtime/TrafficBroadcastService.cs` — 改为SQL聚合或统计累加器
- `src/Aneiang.Yarp.Dashboard/Infrastructure/Yarp/YarpRequestCaptureMiddleware.cs` — **修复采样Bug(ProxyRequest移到ShouldLog后)** + RecyclableMemoryStream + Headers精简(Dictionary→List) + ReadBodyAsync ArrayPool + MinLogLevel修复 + Message延迟拼接 + UpstreamPath拼接复用 + TraceId优化 + 截断双重分配修复
- `src/Aneiang.Yarp.Dashboard/Modules/ProxyLog/Services/DownstreamCaptureTransform.cs` — 条件捕获收紧
- `src/Aneiang.Yarp.Dashboard/Modules/Notification/Services/CooldownManager.cs` — 定期清理过期条目
- `src/Aneiang.Yarp.Dashboard/Modules/Waf/Middleware/WafMiddleware.cs` — ReadBodyAsync `new char[]` → ArrayPool<char>
- `src/Aneiang.Yarp.Dashboard/Modules/Waf/Models/WafSecurityEvent.cs` — WafEventStore 持久化改造 + 缓冲区缩减(1000→64) + dequeued前大字段置null + Id改为Guid结构体
- `src/Aneiang.Yarp.Dashboard/Modules/RateLimit/Middleware/RateLimitMiddleware.cs` — Limiters上限降低(10000→1000-2000) + 清理策略改为基于LastAccessedAt淘汰
- `src/Aneiang.Yarp.Dashboard/Modules/RequestRetry/RequestRetryMiddleware.cs` — MemoryStream→RecyclableMemoryStream + ArrayPool.ToArray()矛盾修复
- `src/Aneiang.Yarp.Dashboard/Infrastructure/ConfigProvider/AneiangProxyConfigProvider.cs` — _heartbeats过期清理
- `src/Aneiang.Yarp.Dashboard/Infrastructure/Waf/Services/IpMatcher.cs` — WildcardRegexCache配置变更时重建
- `src/Aneiang.Yarp.Dashboard/Infrastructure/Realtime/ChannelSender.cs` — _channelLocks频道删除时移除锁
- `src/Aneiang.Yarp.Dashboard/wwwroot/js/modules/dashboard-logs.js` — 历史标签页 + 详情按需加载
- `src/Aneiang.Yarp.Dashboard/wwwroot/js/core/dashboard-api.js` — 新增API endpoints
- `src/Aneiang.Yarp.Dashboard/wwwroot/js/core/dashboard-i18n.js` — i18n key扩展
- `src/Aneiang.Yarp.Dashboard/Infrastructure/I18n/zh-CN/` 对应json文件 — 新增中文i18n key
- `src/Aneiang.Yarp.Dashboard/Infrastructure/I18n/en-US/` 对应json文件 — 新增英文i18n key

### SQLite 存储层改动

- `src/Aneiang.Yarp.Storage.Sqlite` Schema — 增加 proxy_logs_meta、proxy_logs_body、proxy_log_settings、waf_events_meta 四张表
- 可能需要新增 `IProxyLogRepository` 接口在 `src/Aneiang.Yarp.Storage/` 中
- `src/Aneiang.Yarp.Extensions/AneiangYarpServiceCollectionExtensions.cs` — 注册新 Repository

### 需新增的 NuGet 依赖

- `Microsoft.IO.RecyclableMemoryStream`（MIT License，微软官方库）

---

## 十、配置示例

### appsettings.json 初始默认值

```json
{
  "Gateway": {
    "Dashboard": {
      "EnableProxyLogging": true,
      "EnableProxyRequestBodyCapture": false,
      "EnableProxyResponseBodyCapture": false,
      "LogBufferCapacity": 50,
      "LogPersistenceEnabled": true,
      "LogMetaRetentionDays": 7,
      "LogBodyRetentionDays": 3,
      "LogMaxBodyLength": 8192,
      "LogMaxBodyBufferBytes": 65536,
      "EnableLogSampling": false,
      "LogSamplingRate": 1.0,
      "LogErrorsOnly": false
    }
  }
}
```

### UI 修改后 SQLite 中存储的运行时配置

```sql
-- proxy_log_settings 表内容（UI 修改覆盖 appsettings.json 默认值）
INSERT INTO proxy_log_settings VALUES ('LogPersistenceEnabled', 'true');
INSERT INTO proxy_log_settings VALUES ('LogMetaRetentionDays', '7');
INSERT INTO proxy_log_settings VALUES ('LogBodyRetentionDays', '3');
INSERT INTO proxy_log_settings VALUES ('LogBufferCapacity', '50');
INSERT INTO proxy_log_settings VALUES ('EnableProxyRequestBodyCapture', 'false');
INSERT INTO proxy_log_settings VALUES ('EnableProxyResponseBodyCapture', 'false');
INSERT INTO proxy_log_settings VALUES ('LogMaxBodyLength', '8192');
INSERT INTO proxy_log_settings VALUES ('EnableLogSampling', 'false');
INSERT INTO proxy_log_settings VALUES ('LogSamplingRate', '1.0');
INSERT INTO proxy_log_settings VALUES ('LogErrorsOnly', 'false');
```

---

## 十一、与参考方案的关键差异总结

| 变化点 | 参考方案 (Andsoon.Yarp) | 当前项目 (Aneiang.Yarp) | 说明 |
|--------|------------------------|------------------------|------|
| **YarpEventSourceListener** | 存在，需删除 | **不存在** | 当前项目只有 ProxyRequest + ProxyResponse，无需删除 YarpEvent |
| **LogSanitizer 优化** | 需改为 static + cached HashSet | **已优化** | 当前项目已使用 static `_jsonOptions` + cached `_headerBlacklist/_queryBlacklist/_jsonFieldSanitizeList` |
| **ProxyLogStore** | 简单环形缓冲区 | **已优化** | 当前项目已使用 lock-free Interlocked + 位掩码优化 |
| **项目结构** | 平铺式 `Services/Logging/` | **模块化** | 当前项目使用 `Modules/ProxyLog/`、`Modules/Dashboard/` 等模块化结构 |
| **Controller** | `DashboardController` | **DashboardApiController** | 路由前缀不同（`api/` 前缀） |
| **Settings页面** | `SystemSettings.cshtml` | **Settings.cshtml** | 文件名不同 |
| **I18n** | 硬编码在 `DashboardI18n.cs` | **JSON资源文件** | 当前项目使用 `Infrastructure/I18n/zh-CN/` 和 `en-US/` JSON文件 |
| **SQLite存储** | `SharedSqliteStore` | **Aneiang.Yarp.Storage.Sqlite** | 当前项目使用独立的存储层，需通过该层操作SQLite |
| **Operations模块** | 不存在 | **存在** | 当前项目有 `OperationsController` 取5000条做聚合，是额外的内存热点 |
| **TrafficBroadcast** | 不存在 | **存在** | 当前项目有 `TrafficBroadcastService` 每2秒取500条，是额外热点 |
| **CooldownManager** | 不存在 | **存在** | 当前项目有 `CooldownManager._cooldowns` 无限增长风险 |
| **LockFreeStatistics** | 不存在 | **存在但未启用** | 当前项目已有高性能统计基础设施但主流程未使用 |
| **ZeroAllocationLogPool** | 不存在 | **存在但未启用** | 当前项目已有 `LogEntryStruct` 对象池但主流程未使用 |
| **TeeResponseCaptureStream** | MemoryStream + DownstreamCaptureTransform双重缓冲 | **分流设计** | 当前项目使用 TeeStream 零拷贝分流 + DownstreamCaptureTransform 条件捕获 |
| **EnableAsyncLogging** | 不存在 | **已有(默认true)** | 当前项目已有异步日志配置项但未见 Channel 实现 |
| **RequestRetryMiddleware** | 不存在 | **存在** | 当前项目有重试中间件，每次重试创建2个 MemoryStream（原方案遗漏） |
| **采样Bug (ProxyRequest提前写入)** | 不存在 | **存在(P0)** | ShouldLog 检查之前 ProxyEntry 已写入，采样/过滤完全失效（原方案遗漏） |
| **TraceId.ToString()分配** | 不存在 | **存在** | 每请求 ActivityTraceId.ToString() + Guid.NewGuid().ToString() 创建临时字符串 |
| **UpstreamPath重复拼接** | 不存在 | **存在** | ProxyRequest + ProxyResponse 各拼一次 Path+QueryString |
| **Headers Dictionary过重** | 不存在 | **存在** | 2个 Dictionary<string,string> 的 buckets+entries 数组开销远超内容 |
| **_heartbeats/WildcardRegexCache/_channelLocks** | 不存在 | **存在(无清理)** | 3个 ConcurrentDictionary 无清理机制（原方案遗漏） |
| **NotifySubscribers lock** | 不存在 | **存在** | 热路径上 lock + ToArray()（原方案遗漏） |
| **SQLite WAL checkpoint** | 不定义 | **缺失** | 原方案未定义 checkpoint 策略，WAL 文件可能无限增长 |

---

## 十二、已知安全模块清单（无需改造）

以下模块已经具备内存安全保护机制，实施时无需额外改造，但应作为"已知安全点"记录：

| 模块 | 安全机制 | 说明 |
|------|---------|------|
| `ProxyLogStore` | lock-free + 位掩码优化 | 已使用 `Interlocked.Increment` + `FastModulo` 位掩码环形缓冲区，性能优于传统 ConcurrentQueue |
| `LogSanitizer` | static `_jsonOptions` + cached HashSet | `JsonSerializerOptions` 和 `_headerBlacklist/_queryBlacklist/_jsonFieldSanitizeList` 均为 static 字段，不会每请求重复分配 |
| `CircuitBreakerMiddleware._circuits` | 3小时清理 + MaxCircuitCount上限 | `TryCleanupStaleCircuits` 方法每3小时清理过期 Circuit，`MaxCircuitCount` 限制为1000，有自我保护机制 |
| `PooledStringBuilder` | ArrayPool-based 对象池 | 已存在 `System.Buffers.ArrayPool<char>` 复用机制，可直接使用 |
| `LockFreeStatistics` | 缓存行对齐 + 条纹锁字典 + SIMD | 已存在高性能统计基础设施（`ConcurrentIntDictionary` + `StripedLock`），但主流程未启用 |
| `ZeroAllocationLogPool` | LogEntryStruct(128B) + ArrayPool对象池 | 已存在零分配日志路径基础设施，但主流程仍使用 class 版 LogEntry |
| `ReadStreamAsync` (YarpRequestCaptureMiddleware) | `ArrayPool<char>.Shared.Rent` | 已使用 ArrayPool 读取响应流，无需改造 |
| `DownstreamCaptureTransform` | 条件捕获分流 | 已有 `ShouldCaptureDownstream` 条件判断，仅对匹配的 ContentType 捕获下游响应 |

| `ReadStreamAsync` (YarpRequestCaptureMiddleware) | `ArrayPool<char>.Shared.Rent` | 已使用 ArrayPool 读取响应流，无需改造 |
| `DownstreamCaptureTransform` | 条件捕获分流 | 已有 `ShouldCaptureDownstream` 条件判断，仅对匹配的 ContentType 捕获下游响应 |
| `BuiltinTransformMiddleware` TraceId | 与 YarpRequestCaptureMiddleware 同路径 | 也在每个代理请求上调用 `Activity.Current?.TraceId.ToString()`，需与 3.10 一同优化 |

**注意**：`LockFreeStatistics` 和 `ZeroAllocationLogPool` 虽然存在但未被主流程启用，方案阶段4（4.2-4.3）的任务正是将它们接入主流程。

**⚠️ 补充：已知问题模块（需修复）**

以下模块存在逻辑缺陷或设计问题，实施时必须修复：

| 模块 | 问题 | 严重性 | 对应修复项 |
|------|------|--------|-----------|
| `YarpRequestCaptureMiddleware` ProxyRequest写入时序 | ProxyEntry 在 ShouldLog 之前写入，采样/MinLogLevel 过滤失效 | **P0 严重Bug** | 阶段0 0.1 |
| `ProxyLogStore._count` | 缓冲区满后永远停留在 _bufferLength，成为死代码 | P2 代码缺陷 | 阶段0 0.3 |
| `ProxyLogStoreExtensions.NotifySubscribers` | 热路径上 lock + ToArray() | P2 性能缺陷 | 阶段4 4.14 |
| `RequestRetryMiddleware.ReadRequestBodyAsync` | ArrayPool.Rent + .ToArray() 矛盾 | P2 设计矛盾 | 阶段3 3.14 |
| `DashboardApiController.BuildStatsResponse` | 额外 DateTime List 仅用于 requestsPerMin | P2 冗余分配 | 阶段4 4.16 |
| `DashboardOptions.LogBufferCapacity` | 运行时修改无法动态调整 ProxyLogStore 缓冲区大小 | P2 配置语义 | 阶段4 4.18 |
