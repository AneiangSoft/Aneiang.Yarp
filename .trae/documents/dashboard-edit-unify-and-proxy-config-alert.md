# Dashboard 编辑统一 + 详情宽度修复 + YARP 配置失败感知

## Context

用户提出三个关联需求：
1. 服务集群/路由的「新增」与「编辑」当前用了不一致的 UI（新增是 3 字段简单表单弹窗 `showFormModal`，编辑是表单/JSON 双模弹窗 `showJsonModal`），希望统一为同一弹窗，并把风格优化为「简约而不简单」（卡片分组 + 精简留白）。
2. 集群/路由列表点击展开的详情区域宽度没有铺满父表格。
3. YARP 配置应用失败时（如集群引用了未注册的 `IPassiveHealthCheckPolicy`、SessionAffinity 的 `AffinityKeyName` 为 null），代理起不来但 dashboard 页面毫无提示——用户要"出方案"。

根因调研结论：
- **任务 1**：新增走 [dashboard-clusters.js:1130-1173](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/wwwroot/js/modules/dashboard-clusters.js) 的 `showAddFormModal`（简单表单），编辑走 `showEditModal` → `showJsonModal`（双模）。两条路径不一致。
- **任务 2**：集群表头 8 列、路由表头 8 列，但详情行 `colspan=7`（[dashboard-clusters.js:670](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/wwwroot/js/modules/dashboard-clusters.js#L670)、[dashboard-routes.js:1057](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/wwwroot/js/modules/dashboard-routes.js#L1057)），少跨一列导致详情区不铺满。
- **任务 3**：[DynamicConfigPublisher.Publish](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp/Services/DynamicConfigPublisher.cs) 是 `void`，调 `ApplyFromDynamic` 后不感知 YARP reload 结果；YARP 的 `ProxyConfigManager.ReloadConfigAsync` 异步执行，验证失败抛异常被 YARP 内部 catch 仅记日志，dashboard 层无感知。**YARP 2.3.0 公开了 `IConfigChangeListener.ConfigurationApplyingFailed` 回调 + 可注入的 `IConfigValidator`（默认 `ConfigValidator` 已在 DI，`ValidateRouteAsync`/`ValidateClusterAsync` 返回 `IList<Exception>`，与 YARP 内部同规则）**——这是干净的 hook 点，无需日志文本匹配。

用户已确认：任务1 统一为双模弹窗 + 卡片分组精简风格；任务3 展示位置为「概览页顶部告警条 + 集群/路由列表页提示」。

---

## 任务 1：新增/编辑统一双模弹窗 + 卡片分组风格

### 1.1 新增/编辑统一为 `showJsonModal`

**集群** [dashboard-clusters.js](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/wwwroot/js/modules/dashboard-clusters.js)：
- 改 `showAddModal`（L1130）：直接调用 `showJsonModal`，`schemaType:'cluster'`、`data` 用默认模板（Destinations+LoadBalancingPolicy）、`editableId` 可填且 `readOnly:false`、`onSave` 复用现有 `saveClusterFromJson` 的校验逻辑。
- 删除/废弃 `showAddFormModal`（L1134）与 `_showAddJsonModal`（L1175）的分裂路径，保留保存校验逻辑迁移到新 `showAddModal`。

**路由** [dashboard-routes.js](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/wwwroot/js/modules/dashboard-routes.js)：同上模式，`showAddModal` 改调 `showJsonModal`（`schemaType:'route'`、默认路由模板、`editableId` 可填 routeId）。

编辑入口 `showEditModal`（集群 L1355 / 路由 L1887）已是 `showJsonModal`，保持不变。这样新增与编辑是同一个弹窗 UI，差异仅在 `editableId.readOnly` 与初始数据。

### 1.2 卡片分组 + 精简留白风格

改造 [dashboard-form-builder.js](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/wwwroot/js/editors/dashboard-form-builder.js) 的 `build`/`_buildFields`/`_createField`：
- 顶层字段分组为「卡片」：顶层 object 属性（集群的 `Destinations`/`HealthCheck`/`HttpClient`/`HttpRequest`/`SessionAffinity`/`Metadata`；路由的 `Match`/`Transforms`）各渲染为一张卡片；扁平基本字段（`ClusterId`/`LoadBalancingPolicy`/`Order`）归入「基本信息」卡片。
- 现有 `fieldset.border.rounded.p-3` + `legend`（L117-123）升级为卡片样式：白底、圆角 10px、轻阴影、标题区带图标与下边框分隔。
- 精简：label 字重 500、字段间距 mb-3→合理收紧、输入框圆角统一 8px、对齐 label 宽度。
- array 渲染（L150 `_createArrayInput`）保持逻辑，仅统一卡片内边距与按钮样式。

新增 CSS 到 [_DashboardLayout.cshtml](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/Views/Shared/_DashboardLayout.cshtml)（复用现有 `--primary-color`/`--card-bg`/`--border-color` 变量）：
```
.fb-card { background:#fff; border:1px solid var(--border-color); border-radius:10px;
           padding:14px 16px; margin-bottom:14px; box-shadow:0 1px 3px rgba(0,0,0,0.04); }
.fb-card-title { font-size:13px; font-weight:600; color:var(--text-secondary);
                 display:flex; align-items:center; gap:8px; margin-bottom:12px;
                 padding-bottom:8px; border-bottom:1px solid var(--border-color);
                 text-transform:uppercase; letter-spacing:.4px; }
.fb-field { margin-bottom:10px; }
.fb-field:last-child { margin-bottom:0; }
.fb-label { font-size:13px; font-weight:500; color:#475569; margin-bottom:4px; }
```
- 优化 [dashboard-modals.js](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/wwwroot/js/core/dashboard-modals.js) `showJsonModal` 表单区域容器：`#${id}-form-area` padding 调整、`overflow:auto`、背景 `#f8fafc` 衬托卡片。

---

## 任务 2：详情行宽度铺满

两处 colspan 从 7 改为 8（与表头 8 列对齐）：
- [dashboard-clusters.js:670](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/wwwroot/js/modules/dashboard-clusters.js#L670)：`colspan: '7'` → `'8'`
- [dashboard-routes.js:1057](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/wwwroot/js/modules/dashboard-routes.js#L1057)：`colspan: '7'` → `'8'`

无 CSS 改动（`.detail-panel` 无宽度限制，根因纯粹是 colspan 不匹配）。

---

## 任务 3：YARP 配置应用失败感知

### 3.1 后端：错误捕获 + 存储

**新增 `IProxyConfigErrorStore`**（`src/Aneiang.Yarp/Services/ProxyConfigHealth/`）：
- 内存单例，线程安全（ConcurrentQueue + 锁）。
- 数据模型：`{ Status: 'healthy'|'error', Errors: [{ Id, RouteId?, ClusterId?, Message, ExceptionType, OccurredAt, Source: 'reload'|'prevalidate' }], LastErrorAt, LastHealthyAt }`。
- 保留最近 N 条（如 50），支持 `GetSince(DateTime?)` 增量查询、`MarkHealthy()`、`AddError()`。

**主捕获：`YarpConfigErrorListener` 实现 `IConfigChangeListener`**：
- 实现 `ConfigurationApplyingFailed(IReadOnlyList<IProxyConfig>, Exception)`：解析异常（`AggregateException` 拆解），正则 best-effort 归因 RouteId/ClusterId（从消息文本如 `set on the cluster 'xxx'` 提取），写入 store。
- 覆盖：启动 hosted-service 初始 reload + 运行时所有 reload 失败（YARP 以 `IEnumerable<IConfigChangeListener>` 收集，DI 注册即生效）。

**辅预校验：`DynamicConfigPublisher.Publish` 注入 `IConfigValidator`**：
- 在 [DynamicConfigPublisher.cs:107](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp/Services/DynamicConfigPublisher.cs#L107) 调 `ApplyFromDynamic` 前，对 `publishRoutes`/`publishClusters` 逐个调 `IConfigValidator.ValidateRouteAsync`/`ValidateClusterAsync`。
- 返回的异常列表写入 store（`Source:'prevalidate'`，归因精确到 RouteId/ClusterId）。
- 预校验失败仍继续 `ApplyFromDynamic`（不阻塞 Controller 返回，让 listener 兜底捕获真实 reload 结果），但 store 已有精准错误供列表页定点提示。
- DI：`DynamicConfigPublisher` 构造增加 `IConfigValidator` 参数（YARP 默认 `ConfigValidator` 已注册）。

**DI 注册**（[AneiangYarpServiceCollectionExtensions.cs](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp/Extensions/AneiangYarpServiceCollectionExtensions.cs)）：
- `services.AddSingleton<IProxyConfigErrorStore, ProxyConfigErrorStore>();`
- `services.AddSingleton<IConfigChangeListener, YarpConfigErrorListener>();`（YARP 会自动收集）

### 3.2 后端：API 暴露

- **扩展 `OverviewSnapshot`**（[OverviewSnapshotController.cs](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/Modules/Dashboard/Controllers/OverviewSnapshotController.cs)）：快照增加 `proxyConfigHealth` 字段（status + lastErrorAt + errorCount + 首条错误摘要），随现有 5s SignalR 推送 + HTTP 兜底。
- **新增 `GET api/config/apply-errors?since=&scope=cluster|route`**（新增 `ProxyConfigErrorsController`）：返回增量错误列表（含归因 RouteId/ClusterId），供列表页定点匹配。

### 3.3 前端：概览页告警条 + 列表页提示

**概览页**（`Views/Dashboard/Index.cshtml` + `dashboard-overview.js`）：
- 顶部固定告警条 `#proxy-config-alert`：`proxyConfigHealth.status==='error'` 时显示红色 banner（图标 + 「代理配置应用失败」+ 首条错误摘要 + 时间 + 「查看详情」），点击展开错误列表；`healthy` 时隐藏。
- 复用现有 SignalR/轮询机制刷新。

**集群/路由列表页**（`dashboard-clusters.js`/`dashboard-routes.js`）：
- 页面加载时调 `GET api/config/apply-errors`，按 `ClusterId`/`RouteId` 匹配行：
  - 出问题的行在名称列加橙色警告图标（`bi-exclamation-triangle`，tooltip 显示错误消息）。
  - 列表顶部窄提示条：「N 项配置应用失败，代理可能未生效」。
- 新增/编辑保存后，刷新 apply-errors 并更新标记。

---

## 涉及文件清单

**任务 1**：
- 修改 `wwwroot/js/modules/dashboard-clusters.js`（`showAddModal` 统一）
- 修改 `wwwroot/js/modules/dashboard-routes.js`（`showAddModal` 统一）
- 修改 `wwwroot/js/editors/dashboard-form-builder.js`（卡片分组渲染）
- 修改 `wwwroot/js/core/dashboard-modals.js`（表单区域容器样式）
- 修改 `Views/Shared/_DashboardLayout.cshtml`（新增 `.fb-card` 等 CSS）

**任务 2**：
- 修改 `wwwroot/js/modules/dashboard-clusters.js`（colspan 7→8）
- 修改 `wwwroot/js/modules/dashboard-routes.js`（colspan 7→8）

**任务 3**：
- 新增 `src/Aneiang.Yarp/Services/ProxyConfigHealth/IProxyConfigErrorStore.cs` + `ProxyConfigErrorStore.cs`
- 新增 `src/Aneiang.Yarp/Services/ProxyConfigHealth/YarpConfigErrorListener.cs`
- 修改 `src/Aneiang.Yarp/Services/DynamicConfigPublisher.cs`（注入 IConfigValidator 预校验）
- 修改 `src/Aneiang.Yarp/Extensions/AneiangYarpServiceCollectionExtensions.cs`（DI 注册）
- 新增 `Modules/Dashboard/Controllers/ProxyConfigErrorsController.cs`
- 修改 `Modules/Dashboard/Controllers/OverviewSnapshotController.cs` + 快照 DTO（proxyConfigHealth）
- 修改 `Views/Dashboard/Index.cshtml` + `wwwroot/js/modules/dashboard-overview.js`（告警条）
- 修改 `dashboard-clusters.js`/`dashboard-routes.js`（列表页定点标记 + 顶部提示条）
- i18n：`zh-CN`/`en-US` 的 `core.json` 增加 `proxyConfig.*` 键

## 验证

1. **编译**：`dotnet build Aneiang.Yarp.sln` → 0 错误。
2. **任务 1**：启动 SampleGateway，集群/路由点「新增」→ 弹双模弹窗（表单/JSON 可切）、Cluster/Route ID 可填；点「编辑」→ 同一弹窗、ID 只读；表单模式字段按卡片分组、留白精简。
3. **任务 2**：集群/路由列表点行展开 → 详情区宽度铺满表格。
4. **任务 3**：
   - 保留故障集群 `allclusterprops`（passive health policy 未注册 + affinity key null）。
   - 启动后概览页顶部出现红色告警条，显示配置失败摘要。
   - 集群列表 `allclusterprops` 行显示橙色警告图标，tooltip 含错误消息，顶部提示条显示失败计数。
   - 修正该集群配置保存后，告警条消失、行标记清除（listener 收到成功 reload → store.MarkHealthy）。
5. **回归**：`dotnet run --project tests/Aneiang.Yarp.Regression` 通过（无新回归）。

## 局限

- 真·启动 appsettings 静态配置非法时，YARP `InitialLoadAsync` 直接抛错使进程崩溃，listener 不触发、dashboard 未起——此场景不在覆盖范围（进程都起不来）。方案覆盖的是「动态配置 reload 失败但进程存活」的真实运行场景。
- `IConfigChangeListener` 异常归因到具体 RouteId/ClusterId 靠消息正则 best-effort；`IConfigValidator` 预校验路径归因精确。两者互补。
