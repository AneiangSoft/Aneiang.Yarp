# Aneiang.Yarp.Dashboard 改造方案文档

## 1. 目标

本方案的目标是把当前 `Aneiang.Yarp.Dashboard` 从“可用的运维页面”逐步升级为一个更清晰、更易维护、更适合生产环境的 YARP Dashboard。改造重点包括：

- 解耦 Controller 与业务查询逻辑
- 统一 API 契约与 DTO
- 升级日志模型与日志存储能力
- 增强安全性、脱敏、过滤与采样
- 让鉴权体系更符合 ASP.NET Core 标准能力，同时保留动态开关
- 提升前端可维护性与可用性
- 补齐测试、文档和国际化能力

---

## 2. 改造原则

1. **先稳定，再重构**
   - 先抽离结构，不急着大改 UI。
   - 先把对外契约固定，再逐步优化内部实现。

2. **兼容优先**
   - 默认保持现有 API 路径、页面、配置项可用。
   - 新能力尽量通过扩展方式接入，而不是破坏式替换。

3. **以生产可用为目标**
   - 日志、鉴权、敏感信息处理必须考虑生产场景。
   - 大体积 body、错误请求、采样和脱敏要优先支持。

4. **可测试、可回滚**
   - 每一步都要有明确测试点。
   - 每一阶段都允许独立上线。

---

## 3. 当前问题概览

基于现有代码，Dashboard 当前存在这些典型问题：

- `DashboardController` 负责太多事情，包含查询、拼装、判断和返回。
- `GetClusters()` / `GetRoutes()` 返回匿名对象，契约不稳定。
- 日志存储仍偏“文本化”，缺少结构化维度。
- 请求捕获中间件功能完整，但缺少采样、过滤和脱敏。
- 鉴权虽然灵活，但与 ASP.NET Core 原生授权体系还有距离。
- 前端是多文件脚本，但模块边界和 API 层封装仍可增强。
- 测试覆盖度不够，尤其是 URL 推断、权限判断、映射和日志行为。

---

## 4. 总体改造路线

建议按 4 个阶段推进：

### 阶段 1：结构治理
- 抽离 DTO
- 抽离查询服务
- 抽离映射器
- 收敛 Controller 逻辑

### 阶段 2：安全与日志增强
- 统一鉴权策略
- 增加动态权限服务
- 日志结构化
- 采样、过滤、脱敏

### 阶段 3：前端与体验升级
- 页面模块化
- 列表筛选和详情增强
- 日志关联视图
- 国际化完善

### 阶段 4：工程化收口
- 补测试
- 补 Swagger/OpenAPI
- 补可插拔存储
- 补配置文档与示例

---

## 5. 详细实施方案

## 5.1 第一阶段：结构治理

### 5.1.1 抽离 Dashboard DTO

#### 目标
将目前大量匿名对象返回改成强类型响应模型，稳定对外契约。

#### 建议新增 DTO
- `DashboardInfoResponse`
- `DashboardClusterResponse`
- `DashboardDestinationResponse`
- `DashboardRouteResponse`
- `DashboardLoginResponse`
- `DashboardLogResponse`
- `DashboardLogEntryResponse`
- `DashboardErrorResponse`

#### 实施步骤
1. 在 `src/Aneiang.Yarp.Dashboard/Models/` 新建 DTO 文件。
2. 将 `DashboardController` 中所有 `Json(new { ... })` 的返回逐步替换成 DTO。
3. 保持 JSON 字段名与当前前端兼容，必要时使用 `[JsonPropertyName]`。
4. 先改 `info`、`clusters`、`routes`、`logs` 接口，最后改 `login`。

#### 验收标准
- 所有 API 返回结构固定。
- 前端无需大改即可消费。
- Swagger 可直接识别返回类型。

---

### 5.1.2 抽离查询服务

#### 目标
把 Controller 中对 YARP 状态和动态配置的读取逻辑迁移出去。

#### 建议新增服务
- `IDashboardInfoQueryService`
- `IDashboardClusterQueryService`
- `IDashboardRouteQueryService`
- `IDashboardLogQueryService`
- `IDashboardAuthQueryService`

#### 实施步骤
1. 在 `Services/` 新建查询服务接口和实现。
2. 把 `GetInfo()` 的运行时信息组装逻辑移到 `DashboardInfoQueryService`。
3. 把 `GetClusters()` 的 cluster/destination 组装逻辑移到 `DashboardClusterQueryService`。
4. 把 `GetRoutes()` 的 route/transforms/source 组装逻辑移到 `DashboardRouteQueryService`。
5. 把 `GetLogs()` 和 `ClearLogs()` 逻辑移到 `DashboardLogQueryService`。
6. Controller 只负责路由、参数校验和结果返回。

#### 验收标准
- Controller 文件显著变短。
- 业务逻辑可单测。
- 后续改数据结构不会大面积修改 Controller。

---

### 5.1.3 抽离映射器

#### 目标
统一 cluster / route / log 的映射规则，避免 Controller 里重复构造对象。

#### 建议新增
- `DashboardClusterMapper`
- `DashboardRouteMapper`
- `DashboardLogMapper`

#### 实施步骤
1. 先做内部私有方法版映射器，快速收敛重复逻辑。
2. 如果映射逻辑继续扩大，再提升为独立类。
3. 所有字段映射统一从 DTO 层输出。

#### 验收标准
- `GetClusters()` / `GetRoutes()` 变短。
- 字段变更只需要改一处。

---

### 5.1.4 收敛“是否可编辑”规则

#### 目标
把“是否可编辑”的规则从查询逻辑中独立出来。

#### 建议新增
- `IEditablePolicy`
- `DashboardEditablePolicy`

#### 规则建议
- 静态配置来源默认不可编辑。
- 动态配置来源可编辑。
- 特定环境、命名空间、标签可额外限制。

#### 实施步骤
1. 从 `GetClusters()` / `GetRoutes()` 中提取判断逻辑。
2. 在查询服务中统一调用 `IEditablePolicy`。
3. 允许策略依赖配置、环境变量、动态状态。

#### 验收标准
- 编辑权限规则单一入口管理。
- 后续扩展规则不需要改多个接口。

---

## 5.2 第二阶段：安全与日志增强

### 5.2.1 升级为标准鉴权体系

#### 目标
在保留现有灵活性的前提下，向 ASP.NET Core Authentication / Authorization 体系靠拢。

#### 推荐方式
- Authentication：识别身份
- Authorization：判断是否可访问 Dashboard
- 保留 `AuthorizeRequest` 作为最高优先级扩展点

#### 实施步骤
1. 新增 `DashboardAuthenticationScheme` 或至少定义统一授权策略。
2. 将 API Key、JWT、默认 JWT 拆成可组合的授权处理器。
3. 把“是否放行”判断迁移到 `AuthorizationHandler` 或授权服务中。
4. 保留当前 filter 兼容层，逐步切换内部实现。
5. 对浏览器访问与 XHR 请求分别处理失败响应。

#### 验收标准
- 动态开关仍然可用。
- 支持 `[Authorize]` 风格扩展。
- 后续接入 SSO 更顺滑。

---

### 5.2.2 保留动态权限开关能力

#### 目标
确保权限状态可以随运行时变化即时生效。

#### 建议实现方式
- `IDashboardAccessState`
- `IDashboardPermissionService`
- `IOptionsMonitor<DashboardOptions>`
- 或外部配置源（数据库、Redis、配置中心）

#### 实施步骤
1. 把启动时读取的权限值改为每次请求实时读取。
2. 将动态开关逻辑统一放入权限服务。
3. 在授权处理器中调用该服务。
4. 如果需要缓存，缓存必须支持失效机制。

#### 验收标准
- 修改权限配置后无需重启。
- 下一次请求立即按新规则判断。

---

### 5.2.3 日志结构化

#### 目标
把当前偏字符串日志升级成结构化事件模型，便于筛选、聚合和排障。

#### 建议新增事件模型字段
- `EventType`
- `TraceId`
- `RouteId`
- `ClusterId`
- `UpstreamPath`
- `DownstreamUrl`
- `StatusCode`
- `ElapsedMs`
- `RequestHeaders`
- `ResponseHeaders`
- `RequestBody`
- `ResponseBody`
- `Level`
- `Category`
- `Timestamp`

#### 实施步骤
1. 重构 `LogEntry`，拆出基础字段和可选详情字段。
2. 让 `YarpEventSourceListener` 与 `YarpRequestCaptureMiddleware` 统一写入结构化模型。
3. 日志存储层只接受统一模型，不再拼接纯字符串作为主结构。
4. 前端列表先展示摘要，详情区再展开全部字段。

#### 验收标准
- 能按 route / status / trace 过滤。
- request 和 response 可关联展示。
- 后续适配外部存储更容易。

---

### 5.2.4 加入采样、过滤和脱敏

#### 目标
避免在生产环境里记录过多敏感或无用信息。

#### 建议配置项
- 是否只记录错误请求
- 仅记录指定 route / cluster
- body 最大记录长度
- header 白名单 / 黑名单
- query 白名单 / 黑名单
- JSON 字段脱敏规则
- content-type 白名单
- 采样比例

#### 实施步骤
1. 为 `DashboardOptions` 增加日志采样相关配置。
2. 在 `YarpRequestCaptureMiddleware` 中判断是否记录。
3. 在输出到 `ProxyLogStore` 前做脱敏处理。
4. 提供默认脱敏规则：
   - `Authorization`
   - `Cookie`
   - `Set-Cookie`
   - `ApiKey`
   - `password`
   - `token`
5. 对 body 过长的情况进行截断，并标记截断状态。

#### 验收标准
- 敏感信息不会以明文进入日志。
- 可控制日志规模。
- 生产环境风险显著降低。

---

### 5.2.5 让 `ProxyLogStore` 可插拔

#### 目标
让日志存储从“内存环形缓冲区”升级为可替换架构。

#### 建议接口
- `IProxyLogStore`
- `InMemoryProxyLogStore`
- `FileProxyLogStore`
- `SQLiteProxyLogStore`
- `RedisProxyLogStore`
- `SearchIndexProxyLogStore`

#### 实施步骤
1. 先把当前 `ProxyLogStore` 改造成接口实现。
2. 保留内存实现作为默认方案。
3. 通过 DI 注册不同实现。
4. 日志查询服务只依赖接口，不依赖具体类。
5. 为后续持久化检索预留分页和查询条件。

#### 验收标准
- 默认行为不变。
- 可轻松替换存储实现。

---

## 5.3 第三阶段：前端与体验升级

### 5.3.1 前端 API 层封装

#### 目标
把原生脚本中的请求逻辑统一封装，减少重复代码。

#### 建议拆分
- `dashboard-api.js`
- `dashboard-state.js`
- `dashboard-renderers.js`
- `dashboard-logs.js`
- `dashboard-routes.js`
- `dashboard-clusters.js`
- `dashboard-modals.js`

#### 实施步骤
1. 先把重复的 API 请求封装到统一模块。
2. 把页面状态管理统一到一处。
3. 将列表渲染、详情渲染、弹窗渲染拆开。
4. 保持页面入口脚本尽量薄。

#### 验收标准
- 脚本职责清晰。
- 新页面扩展更容易。

---

### 5.3.2 路由与集群页增强

#### 目标
提高信息密度高页面的可读性和可筛选性。

#### 建议增加
- 搜索 `routeId` / `clusterId`
- 按状态过滤
- 按来源过滤
- transforms 折叠展示
- JSON 高亮展示
- 一键复制配置

#### 实施步骤
1. 页面顶部增加筛选工具栏。
2. 将大 JSON 区块改成折叠详情。
3. 高亮关键字段，例如路径、方法、目标地址、状态。
4. 对 transform 和 metadata 增加复制按钮。

#### 验收标准
- 列表可快速定位目标项。
- 复杂配置不再“堆在一屏里”。

---

### 5.3.3 日志页改造成排障视图

#### 目标
从“看日志”提升为“能排障”。

#### 建议增强
- request / response 配对展示
- traceId 聚合展示
- 按路由过滤
- 下游地址高亮
- 状态码颜色区分
- 响应体 / 请求体懒加载展开
- 一键复制事件详情

#### 实施步骤
1. 让日志列表显示摘要信息。
2. 点击后展开关联 request / response。
3. 加入过滤器和搜索框。
4. 对相同 trace 的事件自动聚合。

#### 验收标准
- 日志页面可快速定位异常请求。
- 能直接看到请求与响应的对应关系。

---

### 5.3.4 国际化完善

#### 目标
让 Dashboard 文案体系更完整，减少硬编码。

#### 实施步骤
1. 统一所有页面文案来源。
2. 确保错误信息、按钮、弹窗、空状态均可国际化。
3. 页面语言选择与 cookie / 配置保持一致。
4. 新增文案时同步维护中英两个版本。

#### 验收标准
- 页面中文、英文一致可切换。
- 不再散落硬编码字符串。

---

## 5.4 第四阶段：工程化收口

### 5.4.1 补齐单元测试

#### 目标
让核心逻辑可持续演进。

#### 优先测试点
- `CreateAuthFilter`
- `BuildDownstreamUrl`
- `ExtractCatchAllValue`
- `ResolveLocale`
- cluster / route 映射
- `ProxyLogStore` 环形缓存
- 脱敏与采样逻辑

#### 实施步骤
1. 先补单元测试。
2. 对可疑逻辑补边界测试。
3. 对中间件和控制器补集成测试。

#### 验收标准
- 关键业务逻辑有回归保护。
- 重构时不容易破坏功能。

---

### 5.4.2 增加集成测试

#### 目标
从接口层验证 Dashboard 的整体行为。

#### 实施步骤
1. 用测试宿主启动 Dashboard 服务。
2. 验证登录接口返回结构。
3. 验证 clusters / routes / logs 接口可访问。
4. 验证 route prefix 生效。
5. 验证鉴权失败时跳转或返回 JSON 正常。
6. 验证请求捕获中间件不影响正常代理链路。

#### 验收标准
- 关键端到端流程有自动化覆盖。
- 每次改造后能快速发现回归。

---

### 5.4.3 增加 Swagger / OpenAPI

#### 目标
让 Dashboard API 更易理解、更易联调。

#### 实施步骤
1. 为公开接口补强类型定义。
2. 加入 API 描述注释。
3. 生成 Swagger 文档。
4. 如果不希望公开 UI，可只保留文档生成能力。

#### 验收标准
- API 契约可被自动发现。
- 前后端联调更省心。

---

### 5.4.4 完善文档与示例

#### 目标
让项目更容易被使用者快速接入。

#### 实施步骤
1. 更新 README 中的 Dashboard 使用示例。
2. 补充 `Gateway:Dashboard` 配置说明。
3. 补充生产环境安全建议。
4. 补充日志采样、脱敏、权限切换的使用说明。
5. 补充最小可运行示例。

#### 验收标准
- 新用户能按文档快速接入。
- 典型场景有明确参考。

---

## 6. 全量里程碑 + 全量任务卡

下面是把前面的方案展开成更完整的“里程碑 + 任务卡”形式。每个任务卡都尽量拆到可独立执行、可独立验证。

### 6.1 里程碑 M1：接口与结构治理完成

#### 目标
把 Dashboard 的接口契约、查询逻辑、映射逻辑、编辑规则收敛到稳定结构，先让代码“可维护”。

#### 任务卡 M1-1：建立 DTO 层
- [ ] 建立 `Models/Dtos` 或 `Models/Responses` 目录
- [ ] 设计统一响应基类（如需要）
- [ ] 定义 `DashboardInfoResponse`
- [ ] 定义 `DashboardClusterResponse`
- [ ] 定义 `DashboardDestinationResponse`
- [ ] 定义 `DashboardRouteResponse`
- [ ] 定义 `DashboardLoginResponse`
- [ ] 定义 `DashboardLogResponse`
- [ ] 定义 `DashboardLogEntryResponse`
- [ ] 定义 `DashboardErrorResponse`
- [ ] 保证字段命名与前端兼容
- [ ] 为必要字段加序列化注解
- [ ] 确认现有页面可继续解析

#### 任务卡 M1-2：抽出信息查询服务
- [ ] 新建 `IDashboardInfoQueryService`
- [ ] 新建 `DashboardInfoQueryService`
- [ ] 移植 `GetInfo()` 的进程/内存/启动时间逻辑
- [ ] 抽出 `IWebHostEnvironment` 读取逻辑
- [ ] 抽出版本号获取逻辑
- [ ] 返回 DTO 而不是匿名对象
- [ ] 补测试

#### 任务卡 M1-3：抽出集群查询服务
- [ ] 新建 `IDashboardClusterQueryService`
- [ ] 新建 `DashboardClusterQueryService`
- [ ] 遍历 `IProxyStateLookup.GetClusters()`
- [ ] 映射 destination 详情
- [ ] 映射 health / httpClient / sessionAffinity / metadata
- [ ] 计算 healthy / unknown / unhealthy 计数
- [ ] 集中处理 `isEditable`
- [ ] 补测试

#### 任务卡 M1-4：抽出路由查询服务
- [ ] 新建 `IDashboardRouteQueryService`
- [ ] 新建 `DashboardRouteQueryService`
- [ ] 遍历 `IProxyStateLookup.GetRoutes()`
- [ ] 解析 match / methods / hosts / query / headers
- [ ] 处理 transforms 映射
- [ ] 处理 route source 识别
- [ ] 计算 `isEditable`
- [ ] 保留 order 排序
- [ ] 补测试

#### 任务卡 M1-5：抽出日志查询服务
- [ ] 新建 `IDashboardLogQueryService`
- [ ] 新建 `DashboardLogQueryService`
- [ ] 统一 `GetLogs()` 读取逻辑
- [ ] 统一 `ClearLogs()` 清理逻辑
- [ ] 统一日志分页/数量参数策略
- [ ] 统一日志 DTO 输出
- [ ] 补测试

#### 任务卡 M1-6：抽出映射器
- [ ] 新建 cluster mapper
- [ ] 新建 route mapper
- [ ] 新建 log mapper
- [ ] 把 controller 中重复的对象构建改成 mapper 调用
- [ ] 保证字段输出稳定
- [ ] 补测试

#### 任务卡 M1-7：抽出可编辑策略
- [ ] 新建 `IEditablePolicy`
- [ ] 新建 `DashboardEditablePolicy`
- [ ] 实现静态配置只读规则
- [ ] 实现动态配置可写规则
- [ ] 预留环境/命名空间/标签规则
- [ ] 集群与路由统一调用
- [ ] 补测试

#### 完成判定
- Controller 显著变薄
- API 返回结构稳定
- 代码可单测
- 页面行为未回归

---

### 6.2 里程碑 M2：鉴权与日志能力升级完成

#### 目标
让 Dashboard 具备更标准的鉴权、更安全的日志能力和更适合生产的观测模型。

#### 任务卡 M2-1：引入标准授权骨架
- [ ] 设计授权策略名
- [ ] 设计 requirement
- [ ] 设计 handler
- [ ] 确认浏览器与 API 两类请求的失败响应
- [ ] 保留现有 filter 作为兼容层
- [ ] 保留自定义委托扩展点
- [ ] 补测试

#### 任务卡 M2-2：动态权限服务
- [ ] 新建 `IDashboardAccessState`
- [ ] 或新建 `IDashboardPermissionService`
- [ ] 支持动态读取配置
- [ ] 支持内存刷新
- [ ] 支持外部配置源
- [ ] 确保修改后立即生效
- [ ] 补测试

#### 任务卡 M2-3：结构化日志模型
- [ ] 拆分 `LogEntry` 字段
- [ ] 增加事件类型
- [ ] 增加 trace 关联字段
- [ ] 增加 route / cluster 关联字段
- [ ] 增加请求与响应详情字段
- [ ] 统一 YARP 事件与请求捕获日志格式
- [ ] 补测试

#### 任务卡 M2-4：请求采样与过滤
- [ ] 增加采样开关
- [ ] 增加错误请求优先记录策略
- [ ] 增加 route 白名单
- [ ] 增加 route 黑名单
- [ ] 增加 content-type 白名单
- [ ] 增加 body 最大长度
- [ ] 增加日志量上限策略
- [ ] 补测试

#### 任务卡 M2-5：敏感信息脱敏
- [ ] 建立 header 脱敏规则
- [ ] 建立 query 脱敏规则
- [ ] 建立 body JSON 字段脱敏规则
- [ ] 为认证相关字段提供默认脱敏
- [ ] 支持自定义脱敏规则
- [ ] 在日志落库/入存储前执行脱敏
- [ ] 补测试

#### 任务卡 M2-6：日志存储可插拔
- [ ] 新建 `IProxyLogStore`
- [ ] 将当前实现迁移为内存版本
- [ ] 保留 ring buffer 行为
- [ ] 预留文件/SQLite/Redis/Search 实现接口
- [ ] 查询层只依赖接口
- [ ] 补测试

#### 完成判定
- 权限切换可动态生效
- 日志有结构化字段
- 敏感信息不会裸露
- 存储层可扩展

---

### 6.3 里程碑 M3：前端体验与页面能力完成

#### 目标
让 Dashboard 从“能看”变成“好用、好查、好定位”。

#### 任务卡 M3-1：前端 API 封装
- [ ] 建立统一 API 层
- [ ] 封装 GET/POST/DELETE 请求
- [ ] 统一错误处理
- [ ] 统一鉴权注入
- [ ] 统一 JSON 解析
- [ ] 统一 loading 状态

#### 任务卡 M3-2：前端状态管理收敛
- [ ] 收敛筛选状态
- [ ] 收敛展开状态
- [ ] 收敛弹窗状态
- [ ] 减少全局变量
- [ ] 将页面状态独立维护

#### 任务卡 M3-3：列表渲染模块化
- [ ] 抽离通用列表渲染函数
- [ ] 抽离 badge 渲染
- [ ] 抽离 JSON 渲染
- [ ] 抽离空状态渲染
- [ ] 抽离复制按钮渲染

#### 任务卡 M3-4：集群页增强
- [ ] 搜索 clusterId
- [ ] 筛选可编辑状态
- [ ] 筛选健康状态
- [ ] 展示 destination 概览
- [ ] 折叠 metadata
- [ ] 折叠 sessionAffinity / healthCheck / httpClient
- [ ] 提供复制配置按钮

#### 任务卡 M3-5：路由页增强
- [ ] 搜索 routeId
- [ ] 搜索 clusterId
- [ ] 筛选来源
- [ ] 筛选方法
- [ ] 筛选 host / path
- [ ] 展示 headers / queryParameters
- [ ] 展示 transforms
- [ ] 提供复制配置按钮

#### 任务卡 M3-6：日志页增强
- [ ] 请求与响应成对展示
- [ ] trace 聚合展示
- [ ] route 过滤
- [ ] status 过滤
- [ ] 下游地址高亮
- [ ] request body 懒加载
- [ ] response body 懒加载
- [ ] 一键复制事件详情
- [ ] 清空日志二次确认

#### 任务卡 M3-7：国际化完善
- [ ] 统一文案来源
- [ ] 页面文案中英双语化
- [ ] 错误提示国际化
- [ ] 空状态国际化
- [ ] 按 cookie / 配置切换语言

#### 完成判定
- 页面更易读
- 页面更易筛选
- 页面更易排障
- 国际化不再零散

---

### 6.4 里程碑 M4：工程化与可交付完成

#### 目标
让 Dashboard 不仅可用，而且更容易长期演进、发布和使用。

#### 任务卡 M4-1：单元测试补齐
- [ ] 授权判断测试
- [ ] 路径推断测试
- [ ] catch-all 解析测试
- [ ] locale 解析测试
- [ ] 映射器测试
- [ ] ring buffer 测试
- [ ] 脱敏测试
- [ ] 采样测试

#### 任务卡 M4-2：集成测试补齐
- [ ] 登录流程测试
- [ ] clusters 接口测试
- [ ] routes 接口测试
- [ ] logs 接口测试
- [ ] route prefix 测试
- [ ] 失败鉴权测试
- [ ] 代理链路不破坏测试

#### 任务卡 M4-3：Swagger / OpenAPI
- [ ] 为接口补注释
- [ ] 为 DTO 补说明
- [ ] 接入 Swagger 生成
- [ ] 确保页面与 API 并存不冲突

#### 任务卡 M4-4：文档与示例
- [ ] 更新 README
- [ ] 更新中文 README
- [ ] 增加配置样例
- [ ] 增加安全说明
- [ ] 增加生产建议
- [ ] 增加 FAQ

#### 任务卡 M4-5：版本兼容与回滚准备
- [ ] 固定 API 字段兼容策略
- [ ] 固定默认配置策略
- [ ] 明确弃用项
- [ ] 保留回滚方案
- [ ] 新旧实现可并存一段时间

#### 完成判定
- 可发布
- 可联调
- 可回归
- 可回滚

---

## 7. 全量任务卡总表

下面是所有任务卡的汇总清单，便于你按顺序勾选。

### M1 结构治理
- [ ] M1-1 DTO 层
- [ ] M1-2 信息查询服务
- [ ] M1-3 集群查询服务
- [ ] M1-4 路由查询服务
- [ ] M1-5 日志查询服务
- [ ] M1-6 映射器
- [ ] M1-7 可编辑策略

### M2 鉴权与日志
- [ ] M2-1 标准授权骨架
- [ ] M2-2 动态权限服务
- [ ] M2-3 结构化日志模型
- [ ] M2-4 请求采样与过滤
- [ ] M2-5 敏感信息脱敏
- [ ] M2-6 日志存储可插拔

### M3 前端体验
- [ ] M3-1 前端 API 封装
- [ ] M3-2 前端状态管理
- [ ] M3-3 列表渲染模块化
- [ ] M3-4 集群页增强
- [ ] M3-5 路由页增强
- [ ] M3-6 日志页增强
- [ ] M3-7 国际化完善

### M4 工程化收口
- [ ] M4-1 单元测试
- [ ] M4-2 集成测试
- [ ] M4-3 Swagger / OpenAPI
- [ ] M4-4 文档与示例
- [ ] M4-5 版本兼容与回滚准备

---

## 8. 推荐执行顺序

建议按以下顺序推进：

1. M1-1 ~ M1-7
2. M2-3 ~ M2-6
3. M2-1 ~ M2-2
4. M3-1 ~ M3-7
5. M4-1 ~ M4-5

原因是：

- 先治理结构，后增强能力
- 先把日志和安全链路变稳，再做 UI
- 最后用测试、Swagger 和文档收口

---

## 9. 每个任务卡的统一完成标准

每个任务卡完成时建议同时满足：

- 能编译
- 能测试
- 现有行为不回归
- 对外契约已确认
- 配置有默认值
- 失败路径有处理
- 可以独立回滚

---

## 10. 备注

这个版本已经把之前提到的所有改造点全部展开为“里程碑 + 任务卡”的形式。后续如果你愿意，还可以继续升级成：

- 按文件拆分版
- 按 PR 拆分版
- 按周计划拆分版
- 按人天估算版

---

## 11. 基于开发运维视角的集群与路由新增/修改方案

这一部分专门解决当前最痛的点：**新增、修改集群和路由**。目标不是单纯把表单做出来，而是把它做成一个真正适合开发运维人员使用的配置工作台，解决“难找、难改、难验证、难回滚、难排错”这几个核心问题。

### 11.1 设计目标

#### 11.1.1 面向开发运维人员，而不是面向普通业务用户
Dashboard 里的新增和修改能力，主要服务于以下角色：
- 开发人员：快速把接口接入网关，验证路由、转发、权限、超时、重试等配置
- 运维人员：在生产环境中安全地调整路由、切换目标、临时下线或恢复集群
- 平台人员：批量管理配置，确保变更可追踪、可回滚、可审计

#### 11.1.2 核心目标
- 新增配置时尽量少填无关字段
- 修改配置时能快速定位差异
- 变更前能看到预览和影响范围
- 变更后能立即验证
- 变更失败能回滚到上一个稳定版本
- 静态配置和动态配置要有明确区分

#### 11.1.3 设计原则
- 默认安全：不允许“无意识地修改生产静态配置”
- 逐步引导：复杂配置分层展开，不一次性铺满所有字段
- 可回滚：每次保存都生成变更记录
- 可验证：保存后能立刻刷新并验证
- 可审计：谁改了什么、什么时候改的、改前改后是什么，都要保留

---

### 11.2 业务模型重新定义

#### 11.2.1 集群模型分层
集群编辑不要直接暴露底层 YARP 全量 JSON，而是分层：

- 基础信息层
  - clusterId
  - 是否启用
  - 描述
  - 来源（静态 / 动态）

- 目标节点层
  - destination name
  - address
  - health
  - host
  - metadata

- 策略层
  - load balancing policy
  - session affinity
  - health check
  - HTTP client
  - HTTP request

- 高级层
  - metadata
  - 自定义扩展字段
  - 只读提示

#### 11.2.2 路由模型分层
路由编辑也不要直接暴露全部原始结构，而是分层：

- 基础信息层
  - routeId
  - clusterId
  - path
  - methods
  - hosts
  - order
  - 描述
  - 来源（静态 / 动态）

- 访问控制层
  - authorizationPolicy
  - corsPolicy
  - rateLimiterPolicy
  - timeoutPolicy
  - outputCachePolicy

- 匹配条件层
  - headers
  - query parameters
  - path patterns

- 转发变换层
  - transforms
  - path prefix / remove prefix / set path
  - query / header transform

- 高级层
  - metadata
  - 自定义扩展字段
  - 只读提示

---

### 11.3 核心交互流程设计

#### 11.3.1 新增集群流程
新增集群建议采用“向导式”而不是一次性长表单。

##### Step 1：基础信息
用户先填：
- clusterId
- 描述
- 是否启用

##### Step 2：目标节点
- 至少一个 destination
- 支持添加多个 destination
- 每个 destination 支持地址、host、metadata

##### Step 3：策略配置
- 负载均衡策略
- session affinity
- health check
- http client
- http request

##### Step 4：预览与保存
- 展示最终 JSON 预览
- 展示影响范围
- 展示保存后会影响哪些路由
- 确认后保存

##### Step 5：保存后验证
- 自动刷新集群列表
- 自动检测目标节点状态
- 提示是否可达
- 提示是否需要路由绑定

#### 11.3.2 修改集群流程
修改集群不要直接进入空白表单，而是：
- 先展示原始配置
- 再展示“差异编辑模式”
- 支持只改必要字段
- 支持一键恢复到上一个版本

##### 修改时的关键能力
- 显示变更 diff
- 显示修改前后值
- 显示受影响路由
- 提供保存前预览
- 提供回滚按钮

#### 11.3.3 新增路由流程
新增路由建议采用“先路由、后转发、再策略”的顺序。

##### Step 1：基础路由
- routeId
- path
- clusterId
- methods
- hosts
- order

##### Step 2：匹配规则
- headers
- query parameters
- path 模式

##### Step 3：转发变换
- transforms
- path rewrite
- query rewrite
- header rewrite

##### Step 4：访问控制与策略
- authorizationPolicy
- corsPolicy
- timeoutPolicy
- rateLimiterPolicy
- outputCachePolicy

##### Step 5：预览与保存
- 展示最终匹配表达式
- 展示会命中的目标集群
- 展示 route 影响范围
- 保存后立即刷新验证

#### 11.3.4 修改路由流程
修改路由更适合做成“可对比编辑”。

##### 核心要求
- 改之前先看原值
- 改之后即时看差异
- 保存前看结构化预览
- 需要时可以回滚

---

### 11.4 配置编辑器设计

#### 11.4.1 左右分栏编辑器
建议使用左右分栏：
- 左侧：表单编辑
- 右侧：JSON 预览 / diff / 校验结果

#### 11.4.2 分段折叠
配置编辑器不要一屏铺满，建议分段：
- 基础
- 目标节点
- 策略
- 匹配
- 变换
- 高级

#### 11.4.3 智能补全
编辑器应尽量提供：
- 下拉选择策略枚举
- path 模式提示
- transforms 模板
- destination 地址格式校验
- methods/hosts 多选输入

#### 11.4.4 JSON 模式
对于高级用户，提供 JSON 模式：
- 可直接编辑完整结构
- 但必须经过 schema 校验
- 必须支持格式化和错误高亮
- 输入时要基于 `ConfigurationSchema.json` 提供代码提示
- 提示内容应包含字段名、层级、枚举值、默认值、类型约束
- 在光标停留或输入过程中给出联想提示，尽量让体验接近 IDE 的 JSON 编辑能力
- 复杂对象与数组结构要支持自动补全骨架
- 校验错误要能定位到具体路径
- 如果用户输入的是标准 YARP 配置，应尽可能无缝识别并给出兼容提示

#### 11.4.5 表单模式
对于日常操作，默认使用表单模式：
- 减少误操作
- 降低学习成本
- 只暴露常用字段

---

### 11.5 保存、校验与回滚机制

#### 11.5.1 保存前校验
保存前必须校验：
- routeId / clusterId 唯一性
- address 格式
- path 合法性
- methods 合法性
- transforms 合法性
- 目标 cluster 是否存在
- 依赖关系是否闭环

#### 11.5.2 保存时预检
保存时应提供“预检”能力：
- 先验证结构是否合法
- 再验证目标是否可达
- 再判断是否与现有配置冲突

#### 11.5.3 版本化保存
每次保存都生成版本：
- 版本号
- 修改人
- 修改时间
- 修改原因
- 变更摘要
- diff 内容

#### 11.5.4 一键回滚
回滚不是简单撤销，而是回到指定版本：
- 显示版本列表
- 选择某个版本
- 查看差异
- 确认回滚
- 回滚后再次验证

---

### 11.6 权限与安全控制

#### 11.6.1 编辑权限分级
编辑权限至少分为：
- 只读
- 可编辑路由
- 可编辑集群
- 可发布变更
- 可回滚变更
- 可管理静态配置

#### 11.6.2 高风险操作保护
以下操作必须二次确认：
- 删除路由
- 删除集群
- 改动生产目标地址
- 清空配置
- 回滚生产版本

#### 11.6.3 敏感字段保护
以下字段必须遮罩或特殊处理：
- token
- password
- apiKey
- authorization
- cookie
- 私网地址

#### 11.6.4 审计记录
所有保存行为都要记录：
- 操作人
- 时间
- IP
- 浏览器信息
- 操作对象
- 变更前后 diff

---

### 11.7 前端页面结构建议

#### 11.7.1 集群页
建议布局：
- 顶部：筛选、搜索、新增按钮
- 中间：集群列表
- 右侧或弹窗：详情与编辑

#### 11.7.2 路由页
建议布局：
- 顶部：筛选、搜索、新增按钮
- 中间：路由列表
- 右侧或弹窗：详情与编辑

#### 11.7.3 编辑器页
建议统一编辑页，而不是页面内硬塞长表单：
- 通过侧边抽屉或独立页面编辑
- 支持保存草稿
- 支持预览 JSON
- 支持 diff 预览

---

### 11.8 页面 BUG 修复方向

针对“新增、修改一直有问题”的情况，常见 BUG 及修复方向如下：

#### 11.8.1 保存后列表不刷新
修复方向：
- 保存成功后主动刷新当前列表
- 同步更新本地状态
- 避免依赖浏览器手动刷新

#### 11.8.2 编辑后字段丢失
修复方向：
- 编辑态要保留原始对象
- 提交时合并原始数据与修改数据
- 不要只提交表单里显示的字段

#### 11.8.3 路由 / 集群切换后状态串位
修复方向：
- 每个编辑弹窗独立状态隔离
- 关闭弹窗时清理状态
- 列表项和编辑项不要共用同一对象引用

#### 11.8.4 JSON 编辑模式与表单模式不同步
修复方向：
- 两种模式必须同源数据
- 切换模式时统一做数据序列化 / 反序列化
- 保证最后提交的数据是同一份结构

#### 11.8.5 新增时默认值不一致
修复方向：
- 每类实体建立统一默认模板
- 默认模板只在一个地方定义
- 前端和后端使用同一份 schema 约束

#### 11.8.6 校验提示不明确
修复方向：
- 用字段级错误提示
- 用表单顶部汇总错误
- 保存失败时返回结构化错误列表

---

### 11.9 建议的前后端接口配合

为了让新增、修改真正可用，前后端接口建议至少补齐：

- `GET /dashboard/clusters`
- `POST /dashboard/clusters`
- `PUT /dashboard/clusters/{clusterId}`
- `DELETE /dashboard/clusters/{clusterId}`
- `GET /dashboard/routes`
- `POST /dashboard/routes`
- `PUT /dashboard/routes/{routeId}`
- `DELETE /dashboard/routes/{routeId}`
- `POST /dashboard/routes/{routeId}/validate`
- `POST /dashboard/clusters/{clusterId}/validate`
- `GET /dashboard/config/versions`
- `POST /dashboard/config/rollback`

这些接口最好配套：
- 验证返回
- 保存返回
- 版本返回
- 差异返回
- 错误列表返回

---

### 11.10 推荐的落地顺序

建议按这个顺序落地：

1. 先做前端数据模型和编辑器分层
2. 再做新增/修改弹窗与页面
3. 再补校验与 diff 预览
4. 再补版本管理和回滚
5. 再补审计和高危操作保护
6. 最后修复边界 BUG 和交互细节

---

### 11.11 这一部分最终应该达到的效果

做到最后，Dashboard 的新增/修改能力应该达到：

- 开发人员能快速接入一个新服务
- 运维人员能安全地调整生产路由和集群
- 平台人员能审计、回滚和批量维护
- 普通用户不会因为复杂配置被误导
- 每次变更都能看见、验证、追踪、恢复

这才是从“管理页面”升级成“运维工作台”的关键。

### 11.12 JSON 模式与配置 Schema 的一致性要求

这一点必须作为硬性约束执行：**JSON 模式新增 / 修改的结构，必须与 `src/Aneiang.Yarp.Dashboard/wwwroot/ConfigurationSchema.json` 保持一致**。前端展示给用户的 JSON 编辑体验，要让用户产生“这就是同一套 YARP 配置文件”的感觉，而不是一份独立、割裂的 Dashboard 私有格式。

#### 11.12.1 设计要求
- JSON 模式必须直接遵循 `ConfigurationSchema.json` 中的结构约束
- 前端 JSON 编辑器的字段名、层级、数组结构、默认值描述，要尽量与 schema 完全一致
- Dashboard 不能再维护一套“仅供页面使用”的私有 JSON 结构
- 集群、路由、Dashboard 设置，必须都以同一套配置 schema 语义来呈现

#### 11.12.2 前端交互要求
- 表单模式和 JSON 模式必须共享同一份数据模型
- 从表单切到 JSON 时，显示的内容必须是 schema 对应的原始配置结构
- 从 JSON 切回表单时，必须通过同一份 schema 重新解析与校验
- 任何字段展示、提示、默认值，都应尽量直接取自 schema

#### 11.12.3 校验要求
- JSON 模式保存前必须进行 schema 校验
- 校验失败时要返回结构化错误信息，指出字段路径和原因
- 校验提示要尽量贴近 `ConfigurationSchema.json` 的语义
- 对于枚举、数组、数值范围、必填字段等，必须严格遵守 schema

#### 11.12.4 导入 / 导出要求
导入 / 导出必须和 schema 保持一致，并满足以下要求：

- 导入时支持直接导入标准 YARP 配置文件
- 导出时输出的内容必须是标准 YARP 配置格式，而不是 Dashboard 私有格式
- 导出的文件应可直接交给别人使用，尽量无需二次转换
- 导出内容应与 `ConfigurationSchema.json` 的字段组织方式保持一致
- 对于 Dashboard 额外扩展的能力，应尽量通过兼容字段、注释或扩展区呈现，而不要破坏标准 YARP 配置结构

#### 11.12.5 用户体验要求
- 让用户感觉“我是在编辑 YARP 官方风格配置”
- 让用户可以从外部粘贴标准配置进来直接编辑
- 让用户可以编辑完直接导出给其他项目复用
- 让高级用户能切 JSON，普通用户能切表单，但二者本质上是同一个 schema

#### 11.12.6 落地建议
- `json-editor.js` 必须基于 `ConfigurationSchema.json` 做表单生成、字段提示和校验
- 导入逻辑要优先接受标准 YARP 配置，再映射到 Dashboard 的内部模型
- 导出逻辑要优先输出标准 YARP 配置，再补充 Dashboard 需要的包装结构
- 相关测试必须覆盖导入、导出、schema 校验和字段兼容性

---

### 11.13 页面级最终目标

页面最终要达到的目标是：

- JSON 模式像标准 YARP 配置文件一样自然
- 表单模式与 JSON 模式完全一致
- 导入导出可以互相闭环
- 用户可以直接把 YARP 配置带进来，也可以直接把编辑结果带出去
- Dashboard 既是管理界面，也是标准配置工作台

---

## 12. 文件级改造推荐顺序

---

## 12. 文件级改造推荐顺序

建议按以下顺序动手：

1. `DashboardOptions.cs`
2. `LogEntry.cs`
3. `ProxyLogStore.cs`
4. `DashboardController.cs`
5. `DashboardServiceCollectionExtensions.cs`
6. `YarpRequestCaptureMiddleware.cs`
7. `DownstreamCaptureTransform.cs`
8. `YarpEventSourceListener.cs`
9. `DashboardAuthFilter.cs`
10. `DashboardJwtHelper.cs`
11. `DashboardI18n.cs`
12. `dashboard-core.js`
13. `dashboard-logs.js`
14. `dashboard-routes.js`
15. `dashboard-clusters.js`
16. `dashboard-modals.js`
17. `json-editor.js`
18. `Index.cshtml`
19. `Login.cshtml`
20. `_DashboardLayout.cshtml`
21. `ConfigurationSchema.json`

这个顺序的原则是：
- 先改模型，再改服务
- 先改后端稳定层，再改前端展示层
- 先改共享基础，再改页面细节

---

## 13. 文件级验收总表

每个文件改造完成后，都建议至少检查以下内容：

- 编译通过
- 相关单测通过
- 页面或接口仍可正常访问
- 旧字段兼容性可接受
- 新字段/新行为有默认值
- 日志、鉴权、国际化不出现回归

---

## 14. 备注

如果你愿意，下一步我可以继续把这份文件级清单再细化成：

- **方法级执行清单**：精确到每个函数怎么改
- **PR 切分清单**：每个 PR 改哪些文件
- **周计划清单**：每周交付哪些文件和能力
- **先后依赖图**：哪些文件必须先改、哪些可以并行
