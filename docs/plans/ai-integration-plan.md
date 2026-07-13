# Aneiang.Yarp AI 集成方案

## 整体架构

新增 `Aneiang.Yarp.Dashboard.Modules.AI` 模块，作为 Dashboard 插件集成。AI 功能以"可选插件"形式存在，未配置 API Key 时自动禁用，不影响现有功能。

```
Dashboard (前端)                后端
+------------------+     +---------------------------+
| Chat Widget (JS) |---->| AIController (SSE stream) |
+------------------+     |     |                     |
                         |     v                     |
                         | ChatService               |
                         |   -> GatewayContextProvider|
                         |   -> LLMClient            |
                         |   -> FunctionExecutor     |
                         +---------------------------+
                                    |
                         +---------------------------+
                         | BackgroundAIAnalysis      |
                         |   -> Log Anomaly Detection |
                         |   -> Daily Digest          |
                         |   -> Smart Notifications   |
                         +---------------------------+
                                    |
                         +---------------------------+
                         | IAIProvider (抽象层)        |
                         |   -> OpenAIProvider        |
                         |   -> DeepSeekProvider      |
                         |   -> QwenProvider          |
                         +---------------------------+
```

---

## Task 1: AI Provider 抽象层

**目标**: 统一云端 LLM API 调用，支持 OpenAI / DeepSeek / Qwen 等兼容 OpenAI 协议的提供商。

**新建文件**:
- `Modules/AI/Providers/IAIProvider.cs` — 统一接口
- `Modules/AI/Providers/OpenAICompatibleProvider.cs` — 基于 OpenAI 协议的通用实现（DeepSeek/Qwen 均兼容）
- `Modules/AI/AIOptions.cs` — 配置模型
- `Modules/AI/AIPlugin.cs` — 插件注册（复用 IGatewayPlugin 体系）

**AIOptions 配置**:
```csharp
public class AIOptions
{
    public const string SectionName = "Gateway:Dashboard:AI";
    public bool Enabled { get; set; } = false;
    public string Provider { get; set; } = "openai"; // openai / deepseek / qwen
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public string AnalysisModel { get; set; } = "gpt-4o-mini"; // 后台分析可用更便宜的模型
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.3;
    public int MaxConversationHistory { get; set; } = 20;
    public bool EnableBackgroundAnalysis { get; set; } = true;
    public TimeSpan AnalysisInterval { get; set; } = TimeSpan.FromHours(1);
}
```

**IAIProvider 接口**:
```csharp
public interface IAIProvider
{
    Task<AIChatResponse> ChatAsync(AIChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<string> ChatStreamAsync(AIChatRequest request, CancellationToken ct = default);
    bool IsAvailable { get; }
}
```

**DI 注册** (`AneiangYarpServiceCollectionExtensions`):
```csharp
// AI module registration (opt-in)
if (config.GetSection(AIOptions.SectionName).GetValue<bool>("Enabled"))
    services.AddAneiangYarpAI(config);
```

---

## Task 2: Dashboard ChatBot — 后端服务

**目标**: 提供对话式运维接口，AI 能查询网关实时状态并执行操作。

**新建文件**:
- `Modules/AI/Services/ChatService.cs` — 对话管理 + Function Calling
- `Modules/AI/Services/GatewayContextProvider.cs` — 收集网关实时上下文
- `Modules/AI/Services/FunctionExecutor.cs` — AI Function Calling 执行器
- `Modules/AI/Controllers/AIController.cs` — SSE 流式接口

**GatewayContextProvider** — 为 AI 提供网关当前状态:
```csharp
// 收集以下数据作为 AI 的 system prompt context:
// - 路由/集群数量和列表摘要
// - 最近 5 分钟 QPS、错误率、P95 延迟
// - Top 5 错误路由
// - 熔断器状态列表
// - 最近 WAF 拦截事件
// - 插件启用状态
public class GatewayContextProvider
{
    public Task<string> BuildContextAsync(CancellationToken ct);
}
```

**FunctionExecutor** — 支持 AI 通过 Function Calling 执行操作:
```
可用函数定义:
1. get_stats(minutes)        — 查询统计数据
2. get_routes(filter)        — 查询路由列表
3. get_clusters()            — 查询集群列表
4. get_circuit_breaker_states() — 查询熔断器状态
5. get_recent_errors(count)  — 查询最近错误日志
6. get_waf_events(count)     — 查询 WAF 安全事件
7. get_notification_history(count) — 查询通知历史
8. create_route(json)        — 创建路由 (需确认)
9. update_rate_limit(routeId, config) — 更新限流配置 (需确认)
```

**AIController** — SSE 流式响应:
```
POST /api/ai/chat       — 流式对话（Server-Sent Events）
GET  /api/ai/status     — AI 模块状态检查
POST /api/ai/analyze    — 手动触发日志分析
```

---

## Task 3: Dashboard ChatBot — 前端 UI

**目标**: 在 Dashboard 中嵌入聊天窗口组件。

**新建文件**:
- `wwwroot/js/modules/dashboard-ai-chat.js` — 聊天模块
- `wwwroot/css/ai-chat.css` — 聊天窗口样式

**修改文件**:
- `Views/Shared/_Layout.cshtml` — 添加聊天按钮和窗口容器

**交互设计**:
- 右下角浮动按钮，点击展开聊天窗口
- 支持 SSE 流式显示 AI 回复
- AI 返回结构化数据时渲染为卡片（路由列表、统计图表、操作确认）
- 快捷指令按钮："/stats" "/errors" "/waf" "/routes"
- 未配置 AI 时隐藏按钮

**前端 API**:
```javascript
// dashboard-api.js 新增
ai: {
    chat: (messages) => fetch('/api/ai/chat', { method: 'POST', body: JSON.stringify({messages}) }),
    status: () => DashboardApi.get('/api/ai/status'),
    analyze: () => DashboardApi.post('/api/ai/analyze')
}
```

---

## Task 4: 智能运维辅助 — 后台分析

**目标**: 后台定期分析日志，发现异常并生成 AI 洞察。

**新建文件**:
- `Modules/AI/Services/BackgroundAIAnalysisService.cs` — 后台分析服务 (BackgroundService)

**分析能力**:

1. **日志摘要**: 每小时从 `IProxyLogRepository` 拉取最近日志，生成结构化摘要（请求量趋势、错误模式、延迟分布变化）
2. **异常检测**: 对比历史基线，检测以下异常:
   - 某路由错误率突增（>3x 基线）
   - 新增未知错误模式
   - 延迟异常飙升
   - WAF 攻击频率突增
3. **配置建议**: 基于流量模式推荐:
   - 限流阈值调整
   - 熔断器参数优化
   - 路由优先级建议

**输出方式**:
- 分析结果写入 `ai_analysis` SQLite 表（新增）
- 严重异常通过 `INotificationService.NotifyCustom("AIAlert", ...)` 推送
- Dashboard Overview 页面显示"AI 洞察"卡片

---

## Task 5: AI 增强通知

**目标**: 通知消息中加入 AI 生成的上下文和建议操作。

**修改文件**:
- `Modules/AI/Services/NotificationEnhancer.cs` — 通知增强器
- `NotificationService.cs` — 在发送前调用增强器（可选）

**工作方式**:
```
原始通知: "Cluster-A 熔断器已打开"
AI 增强后: "Cluster-A 熔断器已打开。原因分析: 最近 5 分钟内目标服务 192.168.1.100:8080 
           返回 12 次 503 错误（占请求的 40%）。建议操作: 
           1. 检查目标服务健康状态 
           2. 确认服务是否正在部署/重启
           3. 考虑增加 recoveryTimeout 配置"
```

**触发条件**: 仅对 Warning/Critical 级别的通知做 AI 增强，Info 级别不增强（避免 API 调用浪费）。

**配置项** (AIOptions 扩展):
```csharp
public bool EnhanceNotifications { get; set; } = false; // 默认关闭
public int NotificationEnhanceCooldownSeconds { get; set; } = 60; // 增强请求限流
```

---

## Task 6: SQLite 存储 + i18n

**新增表**:
```sql
CREATE TABLE IF NOT EXISTS ai_conversations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id TEXT NOT NULL,
    role TEXT NOT NULL,        -- 'user' / 'assistant' / 'system'
    content TEXT NOT NULL,
    function_calls TEXT,       -- JSON
    created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ai_analysis (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    analysis_type TEXT NOT NULL, -- 'log_summary' / 'anomaly' / 'suggestion'
    content TEXT NOT NULL,       -- AI 生成的分析内容 (Markdown)
    severity INTEGER DEFAULT 0,
    related_routes TEXT,
    related_clusters TEXT,
    created_at TEXT NOT NULL
);
```

**新增 Storage 接口**:
- `IAIConversationRepository` — 对话历史 CRUD
- `IAIAnalysisRepository` — 分析结果 CRUD

**i18n**: 新增 `wwwroot/i18n/{locale}/ai.json` 包含聊天组件所有文案

---

## Task 7: Settings 页面 + 插件开关

**新建/修改文件**:
- `Views/AI/AISettings.cshtml` — AI 设置页面（API Key、Provider、Model 选择、功能开关）
- `wwwroot/js/modules/dashboard-ai-settings.js` — 设置页前端
- `plugin-states.json` 新增 `"ai": false` 默认关闭

**设置项**:
- AI 总开关
- Provider 选择 (OpenAI / DeepSeek / Qwen / 自定义)
- API Key (密码框)
- Base URL
- Chat Model / Analysis Model
- 功能开关: ChatBot / 后台分析 / 通知增强
- Token 预算 / 温度参数

---

## 实施顺序与依赖

```
Task 1 (Provider 抽象层) ──┐
                             ├──> Task 2 (ChatBot 后端) ──> Task 3 (ChatBot 前端)
Task 6 (SQLite + i18n) ─────┤
                             ├──> Task 4 (后台分析)
                             └──> Task 5 (通知增强)
Task 7 (Settings 页面) ──────────> 贯穿所有 Task
```

**建议分 3 个迭代交付**:
- **迭代 1**: Task 1 + Task 6 + Task 7 (基础设施，约 2-3 天)
- **迭代 2**: Task 2 + Task 3 (ChatBot MVP，约 3-4 天)
- **迭代 3**: Task 4 + Task 5 (智能运维，约 2-3 天)

**NuGet 依赖**: 
- `System.Net.Http.Json` (已有) — HTTP 调用
- 无需额外 NuGet，OpenAI 协议可直接用 HTTP + SSE 实现

---

## PRO 版本差异化建议

以下 AI 功能可作为 PRO 版本卖点:
- **免费版**: ChatBot 基础问答（只读查询）
- **PRO 版**: Function Calling 写操作、后台分析、通知增强、多 Provider 支持、对话历史持久化
