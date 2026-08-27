# 方案 A：移除 YARP 原生能力插件化，改为直接编辑 YARP JSON 配置

## Context

项目当前把 10 个 YARP 原生能力（route-timeout、route-authorization、route-transforms、route-cors、route-compression、cluster-load-balancing、cluster-health-check、cluster-session-affinity、cluster-http-client、cluster-http-request）通过 `NativePluginAdapters` 封装成"native 插件"，在 Routes/Clusters 详情页用"绑定能力"UI 配置，binding 编译后写回 YARP `RouteConfig`/`ClusterConfig`。

用户决定**去掉这类 native 插件**，让用户在 Routes/Clusters 编辑页**直接编辑 YARP 原生 JSON 配置**（表单 + JSON 双模式）。自研运行时插件（waf/request-retry/rate-limit/rate-limit-redis/circuit-breaker/proxy-log/response-cache/compression/traffic-metrics/cluster-metrics/service-discovery 共 11 个）保持"绑定能力"模型不变。

**预期结果**：native 能力不再走插件绑定，改为列表页"编辑"按钮弹 modal，内嵌打通后的 `DashboardConfigEditor`（表单+JSON 双模式）直接编辑整条 YARP RouteConfig/ClusterConfig；现有 native binding 数据直接丢弃不迁移。

## 关键事实（调研结论）

- **binding 不反写持久化 RouteConfig**：`ConfigEntityMapper.cs:55-71` 确认 RouteConfig/ClusterConfig 来自 `gateway_routes.ConfigJson`/`gateway_clusters.ConfigJson` 列，binding 单独存 `gateway_plugin_bindings` 表，`GatewaySnapshotCompiler` 只在编译 snapshot 时临时合并。所以 native binding 是孤儿，丢弃后需启动清理。
- **`DashboardConfigEditor` 是半成品**：JSON 模式（Monaco）可用，但表单模式坏了——`getSchemaForType()` 在 `DashboardSchemaService` 里不存在（只有 `getSchemaAt(path)`），`DashboardFormBuilder` 不支持 array 类型（无法渲染 transforms）。
- **`ConfigurationSchema.json` 已存在且覆盖 YARP 原生字段**：Match/Transforms/LoadBalancingPolicy/HealthCheck/SessionAffinity/HttpClient/HttpRequest/Destinations。Transforms 是 `array` + `items.anyOf` 区分类型。
- **saveRoute/saveCluster API 已端到端可用**：前端 `dashboard-api.js:255-263` → 后端 `RouteConfigController.cs:38-69` / `ClusterConfigController.cs:38-69`（`PUT /api/config/routes/{routeId}`、`PUT /api/config/clusters/{clusterId}`）。
- **native pluginId 前缀**：`native.route.*` / `native.cluster.*`（如 `native.route.timeout`、`native.cluster.load-balancing`），定义在 `NativePluginAdapters.cs:14-23`。
- **已有 `buildYarpRoute()`/`buildYarpCluster()`**（`dashboard-routes.js:1479`、`dashboard-clusters.js:938`）生成 PascalCase 原生 JSON，可复用为编辑器初始数据来源。

## 实施步骤

### 第 1 步：后端移除 native 插件化

1. **删除文件**：
   - `src\Aneiang.Yarp\Services\NativePluginAdapters.cs`
   - `src\Aneiang.Yarp\Services\NativeAdapters\` 整个目录（10 个 adapter + NativeAdapterHelpers）
2. **改 `src\Aneiang.Yarp\Extensions\AneiangYarpServiceCollectionExtensions.cs`**：移除 `NativePluginAdapters` 作为 `IRoutePluginCompiler`/`IClusterPluginCompiler` 的注册。
3. **改 `src\Aneiang.Yarp\Services\GatewaySnapshotCompiler.cs`**：移除"未注册 native compiler 时自动注入 NativePluginAdapters"的兜底逻辑（约 L141-L145）。`GatewaySnapshotCompiler` 改为只遍历剩余的自研插件 compiler。
4. **改 `src\Aneiang.Yarp.Dashboard\Infrastructure\Plugin\GatewayPluginManager.cs`**：删除 `CreateNativeManifests()` 方法及对 `NativePluginAdapters.Catalog` 的引用，`_manifests` 不再包含 native 插件。
5. **启动清理孤儿 native binding**：在 `GatewayPluginManager.LoadState()`（或新增 IHostedService）中，按 `native.route.`/`native.cluster.` 前缀匹配，从 `gateway_plugin_bindings` 删除这些记录。用现有插件绑定仓储的删除方法。日志记录清理数量。

### 第 2 步：打通 DashboardConfigEditor 表单模式

6. **补 `DashboardSchemaService.getSchemaForType(type)`**（`dashboard-schema-service.js`）：基于现有 `getSchemaAt(path)` 实现——`type==='route'` 返回 `getSchemaAt('ReverseProxy.Routes')` 的 patternProperties 第一项（单条 RouteConfig schema）；`type==='cluster'` 同理取 `ReverseProxy.Clusters`；`type==='full'` 返回整个 schema。
7. **给 `DashboardFormBuilder` 加 array/object 支持**（`dashboard-form-builder.js`）：
   - array 类型：渲染为可增删的项列表，每项递归调用 `_buildFields` 渲染子表单。
   - array + `items.anyOf`（transforms 这种）：每项先选类型（anyOf 分支），再按选中分支的 schema 渲染字段。
   - `collectData` 对应能收集 array/object 数据。
   - **务实边界**：transforms 的 anyOf 分支较多，先覆盖常见类型（PathSet/PathRemovePrefix/RequestHeader/ResponseHeader/RequestHeadersCopy 等），其余分支 fallback 到一个 JSON 子编辑器，保证可用不阻塞。
8. **确认 `DashboardConfigEditor` 的 validate/diff 链路**：`DashboardSchemaValidator`、`DashboardDiffPanel` 是否已实现，缺失则补最小可用版本。

### 第 3 步：Routes/Clusters 列表页加"编辑"入口

9. **`dashboard-routes.js`**：在路由列表每行操作按钮组加"编辑"按钮（`btn btn-sm btn-outline-primary`，符合项目按钮颜色语义：蓝=编辑）。点击弹出 modal，内嵌 `DashboardConfigEditor.init(container, {type:'route', id:routeId, data: buildYarpRoute(route), mode:'form'})`。
10. **`dashboard-clusters.js`**：同理加"编辑"按钮，modal 内嵌 `DashboardConfigEditor.init(container, {type:'cluster', id:clusterId, data: buildYarpCluster(cluster), mode:'form'})`。
11. **保留** `dashboard-clusters.js:931-987` 剥离 `CircuitBreaker/Policy/RateLimit/Waf/Retry` metadata 前缀的逻辑——防止 raw JSON 编辑绕过自研插件系统。编辑器保存的 cluster JSON 走 `PUT /api/config/clusters/{clusterId}`，后端已有处理。

### 第 4 步：i18n

12. **`DashboardConfigEditor` 国际化**（`dashboard-config-editor.js`）：硬编码英文 `'Form Mode'/'JSON Mode'/'Validate'/'Preview Changes'/'Save'/'Cancel'/'Saved successfully'/'Save failed: '` 等改用 `__('editor.*')`。
13. **新增 i18n 键**：在 `Infrastructure\I18n\{zh-CN,en-US}\core.json` 加 `editor.formMode`、`editor.jsonMode`、`editor.validate`、`editor.previewChanges`、`editor.save`、`editor.cancel`、`editor.saveSuccess`、`editor.saveFailed`、`editor.array.addItem`、`editor.array.removeItem`、`editor.transform.selectType` 等。
14. **清理 native 插件相关 i18n**：`plugins.json` 中 10 个 native adapter 的名称/描述键（约 L294-L313）删除。自研插件的 `schema.*` 键全部保留。
15. **i18n 是 EmbeddedResource**：改完 JSON 必须 rebuild 才生效。

### 第 5 步：测试

16. **`tests\Aneiang.Yarp.Regression\Program.cs`**：移除 `TestNativeAdapters` 方法、`InMemoryPluginRepository` 里的 native binding 种子数据、`NativePluginAdapters` 实例化。保留自研插件的回归路径。

## 复用的现有能力（不重造）

- `ConfigurationSchema.json`（YARP 原生字段 schema，已就位）
- `DashboardMonacoEditor`（JSON 模式编辑器）
- `DashboardSchemaService.getSchemaAt(path)`（实现 `getSchemaForType` 的基础）
- `buildYarpRoute()`/`buildYarpCluster()`（编辑器初始数据来源）
- `DashboardApi.endpoints.saveRoute/saveCluster` + 后端 `RouteConfigController`/`ClusterConfigController`（保存链路已通）
- 自研插件的 `DashboardCapabilities` 全套（绑定能力 UI 不动）

## 验证

1. **后端编译**：`dotnet build` 全解决方案通过，无 `NativePluginAdapters` 残留引用。
2. **启动**：`ASPNETCORE_ENVIRONMENT=Development dotnet run --project samples\SampleGateway\SampleGateway.csproj`，日志可见"清理 N 条 native orphan binding"。
3. **绑定能力列表**：Routes/Clusters 详情页"绑定能力"不再出现 10 个 native 插件，只剩自研插件。
4. **编辑入口**：路由列表点"编辑"→ modal 弹出 → 表单模式显示 Match/Transforms/AuthorizationPolicy 等字段 → 切 JSON 模式显示完整 RouteConfig JSON → 改 transforms 项 → 切回表单模式数据保留 → 保存 → 列表刷新看到变化。
5. **集群编辑**：同理验证 LoadBalancingPolicy/HealthCheck/SessionAffinity 等字段表单编辑 + JSON 模式。
6. **自研插件不受影响**：waf/rate-limit 等仍能通过"绑定能力"正常绑定、启停、删除。
7. **回归测试**：`dotnet run --project tests\Aneiang.Yarp.Regression` 通过。

## 技术风险

- **FormBuilder 的 transforms(array+anyOf) 渲染是最大工作量点**。务实路径：通用 array 支持先做，transforms 任何 Of 分支覆盖常见类型，冷门分支 fallback 到项内 JSON 子编辑器，保证不阻塞主流程。
- **表单↔JSON 双向同步**：`switchMode` 时需正确序列化/反序列化，transforms 这种异构数组要保留原值不丢字段。
