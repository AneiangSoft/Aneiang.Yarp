# 插件配置类型不匹配调试记录

- 会话：`plugin-config-type-mismatch`
- 状态：`[OPEN]`
- 症状：路由绑定 `circuit-breaker` 时，`failureStatusCodes[0]` 被后端判定不符合 `integer` 类型。
- 范围：检查路由与服务集群的单个/批量插件绑定，以及所有已安装插件的 schema 配置类型。

## 假设

1. integer/number 数组项被前端按字符串序列化。
2. 单个绑定与批量绑定的序列化逻辑不一致。
3. 其他复杂类型插件存在同类问题。
4. 集群与路由作用域的表单默认值或插件筛选不一致。
5. 后端校验正确，错误发生于前端请求构造。

## 证据

- 用户返回的后端校验错误明确显示 `failureStatusCodes[0]` 实际 JSON token 不符合 `integer`。
- `dashboard-capabilities.js` 的共享 Schema 表单读取器此前将所有基础数组项固定生成为字符串。
- 单个绑定、批量绑定、插件管理页均调用同一 `readSchemaForm()`。
- 后端 `PluginConfigurationSchemaValidator` 严格要求 integer 必须为 JSON Number，此行为正确。
- 当前内置插件中确认受影响的字段：
  - `circuit-breaker.failureStatusCodes`（Cluster）
  - `request-retry.retryOnStatusCodes`（Route）
  - `response-cache.cacheStatusCodes`（Route）
- 其余 manifest 插件仅包含标量、布尔值或 `array<string>`，原转换路径可保持 schema 类型。
- 原生插件无可视化 schema，提交 `{}` 时使用默认配置；高级 JSON 仍由后端强类型校验。
- 原生适配器此前使用 PascalCase 强类型反序列化，而 Dashboard 提交 camelCase；严格未知字段校验会拒绝正常表单字段。
- 原生适配器描述符此前没有配置 Schema，导致单个/批量绑定无法展示配置表单；必填配置只能提交 `{}` 并失败。
- `native.route.transforms` 使用动态字典，公共表单此前无法编辑 `additionalProperties`。

## 修复

- 基础数组现在按 `items.type` 转换 `integer`、`number`、`boolean`，并按 `items.enum` 恢复枚举原始类型。
- 无效数组项在前端表单阶段阻止提交。
- 原生适配器统一使用严格 camelCase 命名策略，继续拒绝未知字段和字符串数字。
- 10 个原生适配器补齐 v1 配置 Schema 和默认值，并注册到原生插件 manifest。
- Schema 表单支持 nullable union 类型和动态字符串字典，覆盖超时、健康检查、HTTP 客户端、请求版本及 transforms 配置。
- 单个绑定、批量绑定和绑定管理页统一取最高 Schema 版本。
- 已移除前端临时 post-fix 调试采样代码。

## 结论

根因已确认并完成最小修复，等待运行环境复测确认。
