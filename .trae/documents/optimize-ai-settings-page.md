# AI 助手设置页面优化方案

## Context（背景）

当前 AI 助手设置页面（Settings.cshtml 的 `#pane-ai`）已实现卡片式服务商选择和状态徽章，但仍存在以下问题：

1. **Bug**：`saveAIConfig()` 中 `hasApiKey: payload.apiKey || true` —— 用户不修改密钥时 apiKey 为空字符串，`'' || true` 恒为 `true`，导致保存后状态徽章总是显示"已就绪"，即使根本没配置密钥。
2. **i18n 缺失**：三个面板标题（"连接配置"、"参数设置"、"高级功能"）和四条字段提示是硬编码中文，未加 `data-i18n`，英文环境下无法翻译。
3. **UX 缺失**：API Key 无显示/隐藏切换；保存按钮无 loading 状态；测试连接用 modal 弹窗而非内联结果；测试按钮在未配置密钥时不禁用。
4. **视觉**：8 张服务商卡片视觉同质化，缺少品牌色区分，难以快速扫视。

## 优化项

### 1. 修复 hasApiKey 状态徽章 Bug
- **文件**：[dashboard-settings.js](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/wwwroot/js/modules/dashboard-settings.js#L608-L630)
- **改动**：`saveAIConfig()` 保存成功后，重新调用 `loadAIConfig()` 从服务端获取准确的 `hasApiKey`，而非客户端臆测。移除 `hasApiKey: payload.apiKey || true` 这行错误逻辑。

### 2. 补全 i18n 硬编码标签
- **文件**：[Settings.cshtml](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/Views/Dashboard/Settings.cshtml#L518-L613)、[zh-CN/ai.json](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/Infrastructure/I18n/zh-CN/ai.json)、[en-US/ai.json](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/Infrastructure/I18n/en-US/ai.json)
- **新增 i18n key**：
  | key | 中文 | English |
  |-----|------|---------|
  | `ai.connection` | 连接配置 | Connection |
  | `ai.parameters` | 参数设置 | Parameters |
  | `ai.advanced` | 高级功能 | Advanced |
  | `ai.chatModelHint` | 用于聊天对话的模型名称 | Model used for chat conversations |
  | `ai.maxTokensHint` | 256 - 32768 tokens | 256 - 32768 tokens |
  | `ai.temperatureHint` | 0 = 精确, 2 = 创造性 | 0 = precise, 2 = creative |
  | `ai.maxHistoryHint` | 1 - 100 条 | 1 - 100 messages |
- **HTML 改动**：将 `<span>连接配置</span>` 等替换为 `<span data-i18n="ai.connection">连接配置</span>`，提示文本同理加 `data-i18n`。

### 3. API Key 显示/隐藏切换
- **文件**：Settings.cshtml（API Key 字段）、_DashboardLayout.cshtml（CSS）、dashboard-settings.js（事件）
- **HTML**：将 API Key 输入框包装为 `input-group`，末尾添加 `bi-eye` / `bi-eye-slash` 切换按钮。
- **JS**：点击按钮切换 `input.type` 在 `password` / `text` 间，同步切换图标。
- **CSS**：复用 Bootstrap `input-group` 原生样式，无需额外自定义。

### 4. 保存按钮 Loading 状态
- **文件**：dashboard-settings.js `saveAIConfig()`
- **改动**：复用项目已有的 `DashboardLoading.setButton(btn, loading, text)` 工具（[dashboard-loading.js#L256](file:///d:/Codes/2026/aneiang-yarp/src/Aneiang.Yarp.Dashboard/wwwroot/js/core/dashboard-loading.js#L256)），保存期间显示 spinner + "保存中..."，完成后恢复。

### 5. 内联测试连接结果面板
- **文件**：Settings.cshtml（新增结果容器）、_DashboardLayout.cshtml（CSS）、dashboard-settings.js（逻辑）
- **HTML**：在操作栏上方新增 `<div id="ai-test-result">` 容器，默认隐藏。
- **JS**：`testAIConnection()` 改为：
  - 记录 `Date.now()` 计算延迟
  - 成功：内联显示绿色结果卡片（✓ 连接成功 · 延迟 XXms · 服务商 · 模型）
  - 失败：内联显示红色结果卡片（✗ + 错误消息）
  - 不再使用 `DashboardModals.showSuccess/showError` 弹窗
- **CSS**：`.ai-test-result` 成功/失败两种样式变体。

### 6. 测试按钮禁用态
- **文件**：dashboard-settings.js
- **改动**：`loadAIConfig()` 和 `updateStatusBadge()` 中，若 `hasApiKey` 为 false 则禁用测试按钮并加 `disabled` 属性；配置密钥后自动启用。

### 7. 服务商卡片品牌色区分
- **文件**：_DashboardLayout.cshtml（CSS）、Settings.cshtml（HTML data 属性）
- **改动**：每张卡片左侧添加 3px 品牌色 accent bar（`border-left` 或伪元素），各服务商品牌色：
  - DeepSeek `#4D6BFE`、OpenAI `#10A37F`、Anthropic `#D97757`、Google `#4285F4`、xAI `#1a1a1a`、GLM `#9333ea`、Qwen `#615CED`、Custom `#6b7280`
- 选中态保持现有 primary 高亮，品牌色 accent 在选中时加深。

## 不做的事
- 不新增 ReasoningEffort / UseCache / Fallback / McpServer 等后端已有但前端未暴露的高级设置（超出"优化"范围，属于新增功能）。
- 不修改服务商列表和模型名（保持现有 2026 市场配置）。

## 验证方式
1. 切换到 Settings → AI 助手 Tab
2. 验证所有面板标题和提示在中文/英文下正确显示
3. 不输入 API Key 直接保存 → 状态徽章应显示"未配置密钥"而非"已就绪"
4. API Key 输入框点眼睛图标 → 可显示/隐藏明文
5. 未配置密钥时测试按钮应禁用；配置后启用
6. 点击测试连接 → 内联显示结果（延迟/服务商/模型），不再弹窗
7. 保存时按钮显示 spinner
8. 各服务商卡片左侧品牌色 accent bar 可见且区分明显
