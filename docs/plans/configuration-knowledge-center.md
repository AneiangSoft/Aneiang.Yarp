# YARP 配置知识中心（Configuration Knowledge Center）方案

> **项目**: Aneiang.Yarp.Dashboard
> **版本**: v2.0
> **日期**: 2026-07-17
> **状态**: 设计完成，待实施

---

## 一、问题背景

### 1.1 现状痛点

| 痛点 | 表现 | 现有能力 |
|------|------|----------|
| **不知道有什么** | 不了解 YARP 支持哪些功能 | AI 助手可回答，但需用户主动提问 |
| **不知道怎么配** | 不清楚字段含义、取值范围、配置示例 | Monaco 编辑器有 Schema 校验，但无引导式帮助 |
| **不知道配得对不对** | 不确定配置是否合理、是否遵循最佳实践 | 无配置健康检查，仅有语法级校验 |

### 1.2 现有资产

| 资产 | 能力 |
|------|------|
| **AI 模块** | 31 个 AI 工具（18 只读 + 13 写入）、流式对话、提示注入防护 |
| **Monaco 编辑器** | JSON Schema 校验、Hover 提示、自动补全 |
| **JSON Schema** | 67KB，覆盖 Routes/Clusters/Transforms/HealthCheck/SessionAffinity |
| **表单构建器** | `dashboard-form-builder.js`，根据 Schema 自动生成表单 |
| **配置管理** | 导入/导出、历史快照、Diff 对比、回滚 |
| **策略系统** | 路由策略/集群策略的创建、应用、取消 |
| **插件系统** | 熔断器、WAF、限流、重试、AI 五大插件 |
| **通知系统** | Webhook 通知、事件规则、告警级别 |
| **I18n** | 中英双语嵌入式资源 |

### 1.3 现有缺陷（需在本方案中修复）

1. **Schema 不完整**：缺失 WAF、限流、熔断器、输出缓存的 Schema
2. **Schema Service 未实现**：`getSchemaForType()` 被调用但未实现；`getSchemaAt()` 不支持 `anyOf`
3. **Schema 加载分裂**：`DashboardModals` 加载独立 Schema 文件，`SchemaService` 加载合并文件，存在两套机制
4. **Schema 缺少元数据**：字段仅有 `description`，缺少 `example`、`recommendation`、`docUrl`
5. **前端文件过大**：`dashboard-routes.js`（119KB）、`dashboard-clusters.js`（93KB）

---

## 二、方案架构

### 2.1 总体架构

```
Configuration Knowledge Center
├── Layer 1: 功能目录页（Feature Catalog）           ← 新增页面 + 服务
├── Layer 2: 配置模板库（Template Library）            ← 新增后端 + 前端
├── Layer 3: 智能配置向导（Smart Wizard）              ← 增强现有 FormBuilder
├── Layer 4: AI 配置助手增强（AI Enhancement）         ← 增强现有 AI 模块（RAG 模式）
├── Layer 5: 配置健康评分（Config Health Score）       ← 新增规则引擎
├── Layer 6: 内联帮助系统（Inline Help）               ← 增强现有 Monaco + Schema
├── Layer 7: Transform 知识库（Transform Guide）       ← 新增专项页面
└── Layer 8: 用户体验增强（UX Enhancement）            ← 空状态引导 + 变更预览 + 错误友好化
```

### 2.2 与现有功能的融合原则

- **不重复造轮子**：向导基于现有 `FormBuilder` 扩展
- **不平行建设**：功能目录的"立即配置"跳转到现有页面
- **增强而非替换**：Monaco Hover/Completion 在现有基础上增强
- **统一数据源**：Schema 作为唯一配置元数据来源

---

## 三、分层详细设计

### Layer 1: 功能目录页（Feature Catalog）

**目标**：让用户一目了然地看到 YARP 的所有能力，一键跳转配置。

#### 页面布局

卡片网格展示 9 大功能：负载均衡、健康检查、熔断器、请求重试、限流、WAF 防火墙、路由转换、策略管理、通知告警。

每个功能卡片包含：
- 功能名称、图标、核心能力描述（2-3 行）
- 关键配置项列表
- "查看示例"按钮 -> 展开完整 JSON 配置示例
- "立即配置"按钮 -> 跳转到对应配置页面
- "问问 AI"按钮 -> 打开 AI 助手并预填问题

#### 数据结构

```csharp
public class FeatureInfo
{
    public string Id { get; set; }                    // "load-balancing"
    public string Name { get; set; }                  // "负载均衡"
    public string Icon { get; set; }                  // "bi-arrow-repeat"
    public string Category { get; set; }              // "可靠性"
    public string Summary { get; set; }               // "5种负载均衡策略"
    public List<string> KeyPoints { get; set; }       // ["PowerOfTwoChoices", "RoundRobin"]
    public string ConfigLocation { get; set; }        // "Cluster.LoadBalancingPolicy"
    public string ExampleConfig { get; set; }         // JSON 示例
    public string DocUrl { get; set; }                // YARP 官方文档链接
    public string ConfigPageUrl { get; set; }         // Dashboard 配置页面 URL
    public bool IsPlugin { get; set; }                // 是否为插件功能
    public string PluginId { get; set; }              // 关联插件 ID
    public List<FeatureOption> Options { get; set; }  // 可选值列表
}
```

#### 实现

- **后端**：`FeatureCatalogService`，嵌入式 JSON 资源（`Infrastructure/Features/features.json`），支持 I18n
- **API**：`GET /api/features`
- **前端**：`Views/Dashboard/Features.cshtml` + `wwwroot/js/modules/dashboard-features.js`（~15KB）

---

### Layer 2: 配置模板库（Template Library）

**目标**：提供开箱即用的配置模板，一键导入后微调即可。

#### 预置模板（8 个）

| 模板名称 | 场景 | 难度 | 包含配置 |
|----------|------|------|----------|
| 微服务 API 网关 | 典型微服务架构 | 初级 | 3 路由 + 3 集群 + 负载均衡 + 健康检查 |
| 灰度发布 | 按权重分流 | 中级 | 2 集群 + 权重路由 + 熔断器 |
| 高可用代理 | 生产级高可用 | 中级 | 主动健康检查 + 熔断 + 重试 + 限流 |
| 静态文件代理 | 前端资源代理 | 初级 | 路径匹配 + 缓存头 + 压缩 |
| WebSocket 代理 | 实时通信 | 中级 | WebSocket 路由 + 超时配置 |
| WAF 安全防护 | 安全加固 | 中级 | WAF 全规则 + IP 黑名单 + 请求大小限制 |
| API 限流保护 | 防刷防爬 | 初级 | 按路由限流 + 冷却期 + 重试 |
| 金丝雀发布 | 渐进式发布 | 高级 | 权重分配 + 健康检查 + 自动回滚 |

#### 模板数据结构

```csharp
public class ConfigTemplate
{
    public string Id { get; set; }                    // "microservice-api-gateway"
    public string Name { get; set; }                  // "微服务 API 网关"
    public string Description { get; set; }
    public string Category { get; set; }              // "基础架构" / "安全" / "高可用"
    public string Difficulty { get; set; }            // "beginner" / "intermediate" / "advanced"
    public List<string> Features { get; set; }        // ["负载均衡", "健康检查"]
    public JsonElement Config { get; set; }           // YARP 标准配置 JSON
    public List<string> Steps { get; set; }           // 导入后的修改步骤指引
    public List<ConfigVariable> Variables { get; set; } // 需要用户填写的变量
}

public class ConfigVariable
{
    public string Key { get; set; }                   // "backend_address"
    public string Label { get; set; }                 // "后端服务地址"
    public string DefaultValue { get; set; }          // "http://localhost:5001"
    public string Description { get; set; }
    public bool Required { get; set; }
}
```

#### 实现

- **后端**：`ConfigTemplateService`，模板以嵌入式 JSON 资源存储（`Infrastructure/Templates/*.json`）
- **API**：
  - `GET /api/config/templates` - 获取模板列表
  - `POST /api/config/templates/{id}/preview` - 预览变更（Dry-Run Diff）
  - `POST /api/config/templates/{id}/apply` - 应用模板
- **前端**：配置历史页面旁新增"模板库"标签页
- **流程**：选择模板 -> 填写变量 -> 预览变更 -> 确认应用

---

### Layer 3: 智能配置向导（Smart Wizard）

**目标**：通过多步骤引导，让用户一步步完成配置创建。

#### 与现有功能的融合

- 复用 `FormBuilder` 的字段生成能力，新增 `wizard` 模式（多步骤分页）
- 完成后调用现有 Save API，不平行实现 CRUD
- 在现有路由/集群页面的"新建"按钮旁增加"使用向导创建"选项

#### 路由创建向导（5 步）

1. **路由标识**：输入 RouteId（实时检查重复，建议 kebab-case）
2. **匹配规则**：路径模式、Host、HTTP 方法（每个字段有 `💡` 提示和 `📖` 文档链接）
3. **目标集群**：选择已有集群或新建（输入后端地址，选择负载均衡策略）
4. **高级选项**：勾选启用熔断器/重试/限流/健康检查/Transform（勾选后展开参数，自动创建策略并应用）
5. **确认创建**：展示将创建的配置摘要 + JSON 预览，确认后一次性创建所有资源

#### 实现

- **前端**：`wwwroot/js/modules/dashboard-wizard.js`（~20KB）
- **后端**：复用现有 `RouteConfigController`、`ClusterConfigController`、`PoliciesController` API
- **FormBuilder 增强**：
  - 新增 `wizard` 模式：字段分组为步骤，支持上一步/下一步
  - 新增字段联动：`visibleWhen` 条件
  - 新增实时校验：输入时检查路由 ID 重复、地址格式

---

### Layer 4: AI 配置助手增强

**目标**：让 AI 从"被动回答"升级为"主动引导的配置专家"。

#### 4.1 RAG 模式知识检索

采用 RAG（检索增强生成）模式，AI 在需要时主动检索知识库，而非将全部知识注入系统提示词。

新增 `ConfigKnowledgeService` 和 AI 工具 `search_config_docs`：

```csharp
public interface IConfigKnowledgeService
{
    Task<KnowledgeResult?> SearchAsync(string query, CancellationToken ct = default);
    Task<List<KnowledgeEntry>> GetAllTopicsAsync(CancellationToken ct = default);
    Task<KnowledgeEntry?> GetTopicAsync(string topicId, CancellationToken ct = default);
}
```

知识库以嵌入式 JSON 资源存储，按 Feature 分文件：

```
Infrastructure/Knowledge/
├── load-balancing.json
├── health-check.json
├── circuit-breaker.json
├── request-retry.json
├── rate-limiting.json
├── waf.json
├── transforms.json
├── session-affinity.json
├── routing.json
└── http-client.json
```

#### 4.2 新增 AI 工具（6 个）

| 工具名 | 类型 | 功能 |
|--------|------|------|
| `search_config_docs` | 只读 | 按关键词检索配置知识库 |
| `get_config_templates` | 只读 | 获取可用配置模板列表 |
| `apply_config_template` | 写入 | 应用指定模板（需确认） |
| `get_feature_guide` | 只读 | 获取指定功能的配置指南 |
| `check_config_health` | 只读 | 分析当前配置的健康度和改进建议 |
| `suggest_configuration` | 只读 | 根据用户描述推荐配置方案 |

#### 4.3 增强系统提示词

在 `BuildSystemPromptAsync` 中增加配置专家角色和检索指引（不注入完整知识库，仅注入检索指引和最佳实践检查清单）。

#### 4.4 AI 主动推送

利用现有 `BackgroundAIAnalysisService` 增强：
- 定期分析配置健康度（每小时，可配置）
- 发现 Critical 问题时通过 `NotificationService` 推送告警
- AI 助手按钮显示建议徽章："💡 有 2 条配置建议"

#### 4.5 自然语言配置示例

| 用户输入 | AI 行为 |
|----------|---------|
| "帮我创建路由，把 /api/orders 转发到 localhost:5003" | 调用 `create_route` 工具 |
| "给 user-service 集群加上熔断器" | 调用 `create_cluster_policy` 工具 |
| "网关返回 502，帮我排查" | 调用 `get_proxy_logs` + `get_health_summary` 诊断 |
| "分析当前配置有什么可以改进的" | 调用 `check_config_health` 工具 |
| "YARP 负载均衡有哪些策略？" | 调用 `search_config_docs` 检索 |

---

### Layer 5: 配置健康评分（Config Health Score）

**目标**：自动分析当前配置，给出评分和改进建议。

#### 规则引擎

```csharp
public interface IConfigHealthRule
{
    string Id { get; }                    // "SEC001"
    string Category { get; }              // Security, Reliability, Performance, BestPractice
    Severity Level { get; }               // Info, Warning, Critical
    string Title { get; }
    string Description { get; }
    string Recommendation { get; }
    string ConfigPageUrl { get; }         // 跳转到配置页面
    Task<HealthCheckResult> EvaluateAsync(GatewayDynamicConfig config, CancellationToken ct);
}
```

#### 预置规则集（12 条）

| 规则 ID | 类别 | 级别 | 检查内容 | 建议 |
|---------|------|------|----------|------|
| SEC001 | 安全 | Critical | WAF 是否启用 | 启用 SQL注入/XSS/路径遍历检测 |
| SEC002 | 安全 | Warning | 是否有 IP 黑名单 | 配置 IP 黑名单 |
| SEC003 | 安全 | Warning | 请求体大小限制 | 设置 MaxRequestBodySize |
| REL001 | 可靠性 | Critical | 每个集群是否配置健康检查 | 添加 Active Health Check |
| REL002 | 可靠性 | Critical | 每个路由是否关联熔断器策略 | 创建熔断器策略并应用 |
| REL003 | 可靠性 | Warning | 集群是否只有单个后端 | 添加至少 2 个后端地址 |
| REL004 | 可靠性 | Warning | 是否启用请求重试 | 创建重试策略 |
| PER001 | 性能 | Info | 负载均衡策略 | 建议使用 PowerOfTwoChoices |
| PER002 | 性能 | Warning | HTTP/2 多路复用 | 启用 EnableMultipleHttp2Connections |
| BP001 | 最佳实践 | Info | 路由 Order 是否设置 | 设置路由优先级 |
| BP002 | 最佳实践 | Info | 是否有配置快照 | 创建配置快照 |
| BP003 | 最佳实践 | Warning | Transform 使用 PathPattern | 建议用 PathPattern 替代 PathRemovePrefix+PathPrefix |

#### 评分算法

```
Score = 100 - Σ(扣分)
Critical: -15 分/项, Warning: -5 分/项, Info: -1 分/项, 最低 0 分
```

#### 实现

- **后端**：`ConfigHealthService` + `IConfigHealthRule` 规则引擎
- **API**：`GET /api/config/health?forceRefresh=false`
- **缓存**：结果缓存 60 秒，配置变更时自动失效
- **前端**：总览页显示健康评分卡片（评分条 + 问题列表 + 快速操作按钮）
- **可扩展**：插件可实现 `IConfigHealthRule` 注册自定义规则

---

### Layer 6: 内联帮助系统（Inline Help）

**目标**：在配置编辑器中提供上下文相关的帮助。

#### 6.1 扩展 JSON Schema 元数据

为每个字段添加 `x-` 扩展属性：

```json
{
  "loadBalancingPolicy": {
    "type": "string",
    "enum": ["PowerOfTwoChoices", "RoundRobin", "LeastRequests", "Random", "FirstAlphabetical"],
    "description": "负载均衡策略",
    "x-example": "RoundRobin",
    "x-recommendation": "生产环境建议 PowerOfTwoChoices",
    "x-docUrl": "https://microsoft.github.io/reverse-proxy/articles/load-balancing.html",
    "x-group": "高级",
    "x-productionRequired": false,
    "x-options": {
      "PowerOfTwoChoices": "选择两个随机目的地，挑请求少的（推荐）",
      "RoundRobin": "轮询分配",
      "LeastRequests": "选择当前请求数最少的",
      "Random": "随机选择",
      "FirstAlphabetical": "按字母顺序选第一个"
    }
  }
}
```

#### 6.2 增强 Monaco Hover Provider

在现有 JSON Schema Hover 基础上注册自定义 Hover Provider，叠加：
- 💡 最佳实践建议（`x-recommendation`）
- 配置示例代码块（`x-example`）
- 可选值说明列表（`x-options`）
- 📖 官方文档链接（`x-docUrl`）

#### 6.3 增强 Monaco Completion Provider

为枚举值补全添加详细文档和"推荐"标记。

#### 6.4 错误信息友好化

拦截 Monaco Schema 验证错误，转换为用户可读提示：

```
❌ 原始: "Property 'clusterId' is required at /routes/order-service"
✅ 改进: "路由 'order-service' 缺少目标集群（clusterId），请指定该路由转发到哪个集群"
```

---

### Layer 7: Transform 知识库（Transform Guide）

**目标**：专项解决 YARP 25+ 种 Transform 类型的认知问题。

#### 页面结构

```
Transform 目录
├── 路径转换
│   ├── PathRemovePrefix - 移除路径前缀
│   ├── PathSet - 设置完整路径
│   ├── PathPrefix - 添加路径前缀
│   └── PathPattern - 模式匹配替换（推荐）
├── 请求头转换
│   ├── RequestHeader - 添加/修改请求头
│   ├── RequestHeaderRouteValue - 从路由值取请求头
│   └── RequestHeadersAllowed - 允许转发的请求头
├── 响应头转换
│   ├── ResponseHeader - 添加/修改响应头
│   └── ResponseTrailer - 添加 Trailer
├── 转发头
│   ├── X-Forwarded - 标准 X-Forwarded-* 头
│   └── Forwarded - RFC 7239 Forwarded 头
├── 查询参数
│   └── QueryValueParameter - 添加查询参数
└── HTTP 方法
    └── HttpMethodChange - 修改 HTTP 方法
```

每种 Transform 展示：
- 用途说明（什么时候用）
- 配置示例（JSON）
- HTTP 请求/响应变化（Before/After 对比）
- 常见搭配建议
- "应用到路由"按钮（选择路由后自动添加此 Transform）

#### 实现

- **数据**：嵌入式 JSON 资源（`Infrastructure/Knowledge/transforms.json`），复用 `ConfigKnowledgeService`
- **前端**：`Views/Dashboard/Transforms.cshtml` + `wwwroot/js/modules/dashboard-transform-guide.js`（~12KB）

---

### Layer 8: 用户体验增强

#### 8.1 空状态引导

首次打开 Dashboard（无路由/集群）时显示引导卡片：

```
┌─────────────────────────────────────────────┐
│            欢迎使用 YARP 网关仪表盘           │
│                                               │
│  你还没有配置任何路由。开始使用：             │
│                                               │
│  📋 从模板创建    🧙 使用向导    ✏️ 手动配置  │
│                                               │
│  💡 建议：点击"从模板创建"快速开始            │
└─────────────────────────────────────────────┘
```

#### 8.2 配置变更预览（Dry-Run）

应用配置前显示变更预览（类似 Git diff）：

```
配置变更预览
──────────────────────────────────────
新增 (2):
  + 路由: order-service (/api/orders/*)
  + 集群: order-cluster (2个后端)

修改 (1):
  ~ 集群: user-cluster
    - 后端: http://localhost:5001
    + 后端: http://localhost:5001, http://localhost:5004

策略 (1):
  + 熔断器: order-cb (自动应用到 order-cluster)

[取消]  [确认应用]
```

#### 8.3 配置收藏和复用

用户可将常用配置保存为"我的模板"，与全局模板库区分：
- 全局模板：项目预置，不可修改
- 我的模板：用户自定义，可编辑/删除/分享

#### 8.4 配置导出为 cURL 命令

选中路由后生成测试用 cURL 命令，方便快速验证配置是否生效。

---

## 四、Schema 修复计划

### 4.1 补全缺失的 Schema

| Schema | 字段范围 | 优先级 |
|--------|----------|--------|
| WAF 配置 | Enabled, EnableSqlInjectionDetection, EnableXssDetection, EnablePathTraversalDetection, EnableIpCheck, IpBlacklist, IpWhitelist, MaxRequestBodySize, MaxHeaderCount | P0 |
| 限流策略 | RequestLimit, WindowSeconds, CooldownSeconds | P0 |
| 熔断器策略 | FailureThreshold, SamplingDuration, MinimumThroughput, RecoveryTimeout, HalfOpenAttempts | P0 |
| 通知规则 | EventType, ChannelIds, CooldownSeconds, RecordToHistory | P1 |

### 4.2 统一 Schema 加载机制

- 废弃独立的 `RouteSchema.json`/`ClusterSchema.json`
- 统一使用 `ConfigurationSchema.json`
- 实现 `getSchemaForType(type)` 方法
- 修复 `getSchemaAt()` 对 `anyOf` 结构的导航支持

### 4.3 扩展 Schema 字段元数据

为所有字段添加 `x-` 扩展属性：
- `x-example`：配置示例
- `x-recommendation`：最佳实践建议
- `x-docUrl`：官方文档链接
- `x-group`：字段分组（基本/高级/安全）
- `x-productionRequired`：是否为生产必选项
- `x-options`：枚举值的详细说明

---

## 五、实施路线图

```
Phase 1 (2周) - 基础设施
  ├── 补全 ConfigurationSchema.json（WAF/限流/熔断/通知）
  ├── 统一 Schema 加载机制，实现 getSchemaForType()
  ├── 修复 getSchemaAt() 对 anyOf 的支持
  ├── 扩展 Schema 字段元数据（x-example/x-recommendation/x-docUrl）
  └── 错误信息友好化（Schema 验证错误 -> 用户可读消息）

Phase 2 (2周) - AI 增强 + 健康评分
  ├── ConfigKnowledgeService（结构化知识库 + RAG 检索）
  ├── 新增 6 个 AI 工具
  ├── 增强系统提示词（检索指引 + 最佳实践检查清单）
  ├── ConfigHealthService（规则引擎 + 12 条预置规则）
  ├── 前端: 总览页健康评分卡片
  └── AI 主动推送建议（BackgroundAIAnalysisService 增强）

Phase 3 (2周) - 功能目录 + 模板库 + Transform 指南
  ├── FeatureCatalogService（功能元数据）
  ├── 前端: 功能目录页（卡片布局 + 立即配置跳转）
  ├── ConfigTemplateService（8 个预置模板）
  ├── 前端: 模板库标签页（一键应用 + 变更预览）
  └── Transform 知识库页面（25+ Transform 文档 + Before/After 对比）

Phase 4 (2周) - 配置向导 + 内联帮助
  ├── 增强 FormBuilder（wizard 模式 + 字段联动）
  ├── 路由创建向导（5 步）
  ├── 集群创建向导（3 步）
  ├── Monaco Hover Provider 增强（示例 + 文档链接）
  ├── Monaco Completion Provider 增强（Transform 智能补全）
  └── 空状态引导（首次使用提示）

Phase 5 (1周) - 打磨
  ├── 配置变更预览（Dry-Run diff）
  ├── 我的模板（用户自定义收藏）
  ├── 配置导出为 cURL 命令
  ├── I18n 国际化
  └── 性能优化（缓存 + 懒加载）
```

---

## 六、新增文件清单

```
src/Aneiang.Yarp.Dashboard/
├── Infrastructure/
│   ├── Features/
│   │   └── features.json                    ← 功能目录元数据
│   ├── Templates/
│   │   ├── microservice-api-gateway.json    ← 模板定义
│   │   ├── canary-release.json
│   │   ├── high-availability.json
│   │   ├── static-files.json
│   │   ├── websocket.json
│   │   ├── waf-security.json
│   │   ├── rate-limiting.json
│   │   └── gray-release.json
│   ├── Knowledge/
│   │   ├── load-balancing.json              ← AI 知识库
│   │   ├── health-check.json
│   │   ├── circuit-breaker.json
│   │   ├── request-retry.json
│   │   ├── rate-limiting.json
│   │   ├── waf.json
│   │   ├── transforms.json
│   │   ├── session-affinity.json
│   │   ├── routing.json
│   │   └── http-client.json
│   └── Health/
│       ├── IConfigHealthRule.cs             ← 规则引擎接口
│       ├── ConfigHealthService.cs            ← 健康评分服务
│       └── Rules/
│           ├── WafEnabledRule.cs
│           ├── HealthCheckRule.cs
│           ├── CircuitBreakerRule.cs
│           └── ...                           ← 12 条规则
├── Modules/
│   ├── Features/
│   │   ├── FeatureCatalogService.cs
│   │   └── IFeatureCatalogService.cs
│   ├── GatewayConfig/
│   │   └── Application/
│   │       ├── ConfigTemplateService.cs
│   │       ├── IConfigTemplateService.cs
│   │       ├── ConfigKnowledgeService.cs
│   │       └── IConfigKnowledgeService.cs
│   └── AI/
│       └── Tools/
│           ├── GatewayToolExecutor.KnowledgeTools.cs   ← 新增 AI 工具执行器
│           └── GatewayToolExecutor.TemplateTools.cs
├── Views/Dashboard/
│   ├── Features.cshtml                       ← 功能目录页
│   ├── Templates.cshtml                      ← 模板库页
│   └── Transforms.cshtml                     ← Transform 指南页
└── wwwroot/js/modules/
    ├── dashboard-features.js                 ← 功能目录页 JS（~15KB）
    ├── dashboard-templates.js                ← 模板库 JS（~10KB）
    ├── dashboard-wizard.js                   ← 配置向导 JS（~20KB）
    ├── dashboard-health-score.js             ← 健康评分卡片 JS（~8KB）
    └── dashboard-transform-guide.js          ← Transform 指南 JS（~12KB）
```

---

## 七、API 清单

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/features` | 获取功能目录列表 |
| GET | `/api/features/{id}` | 获取单个功能详情 |
| GET | `/api/config/templates` | 获取配置模板列表 |
| GET | `/api/config/templates/{id}` | 获取单个模板详情 |
| POST | `/api/config/templates/{id}/preview` | 预览模板应用后的变更 |
| POST | `/api/config/templates/{id}/apply` | 应用模板 |
| GET | `/api/config/health` | 获取配置健康评分报告 |
| GET | `/api/config/knowledge` | 获取所有知识主题 |
| GET | `/api/config/knowledge/{topicId}` | 获取指定主题的配置知识 |
| GET | `/api/config/knowledge/search?q=xxx` | 搜索配置知识库 |

---

## 八、核心收益

1. **降低门槛**：功能目录 + 模板库让新用户 5 分钟内完成首次配置
2. **减少错误**：配置健康评分自动发现缺失的最佳实践
3. **提升效率**：AI 助手支持自然语言配置，无需查阅文档
4. **知识沉淀**：内联帮助让编辑器本身成为学习工具
5. **平滑过渡**：向导引导新用户，Monaco 编辑器满足高级用户
6. **可扩展性**：规则引擎和知识库支持插件扩展
