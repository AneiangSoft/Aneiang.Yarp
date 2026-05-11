# YARP 没有管理界面？这个开源项目直接给你装上！

> 用 YARP 做网关的同学，一定遇到过这些场景：加个路由要改 appsettings.json，改完要重启；改坏了想回滚，翻遍 git log 也找不到完整的历史；想看请求日志，发现 YARP 本身不提供……今天推荐一个开源项目 **Aneiang.Yarp.Dashboard**，专门解决这些痛点。

---

## 装上就能用，3 行代码搞定

不需要数据库，不需要配前端，NuGet 装完就有管理后台。

**第一步：装包**

```bash
dotnet add package Aneiang.Yarp
dotnet add package Aneiang.Yarp.Dashboard
```

**第二步：Program.cs 加两行**

```csharp
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpDashboard();  // ← 加这行

app.UseRouting();
app.UseAneiangYarpDashboard();  // ← 再加这行
```

**第三步：打开浏览器**

访问 `http://localhost:5000/apigateway`，管理后台已经在那了。

不配也能跑，所有配置都有默认值。你的现有 YARP 配置完全不受影响。

---

## 功能一：集群 & 路由管理——再也不用手改 JSON

（此处插入截图：集群管理 / 路由管理界面，展示 JSON 编辑器语法提示）

- **表单创建** — 填几个字段就建好路由或集群
- **JSON 编辑器** — 语法高亮 + 实时校验 + **语法提示**，YARP 官方配置直接粘贴就能用
  - 输入时自动提示可用字段（如 `Match`、`RouteId`、`ClusterId` 等）
  - 自动补全 YARP 标准配置结构
  - 实时校验格式错误，红色波浪线标注
- **智能关联** — 目标集群不存在？自动帮你创建
- **安全删除** — 删路由时可清理孤立集群，删集群自动更新引用路由

---

## 功能二：配置版本管理——改坏了一键回滚

（此处插入截图：配置历史 / 回滚界面）

每次变更（创建/编辑/删除）之前，系统自动保存完整配置快照，记录时间、操作人 IP、变更内容。

改坏了？打开变更历史，选一个版本，点一下回滚。不用慌，不用翻 git log。

另外还支持：
- **导出** — 标准 YARP 格式，可直接贴进 appsettings.json
- **导入** — 自动校验格式，合并后持久化

---

## 功能三：实时请求日志——看清楚每个请求

（此处插入截图：请求日志界面）

经过网关的每个请求都能看到：方法、路径、查询参数、请求/响应头、Body（JSON 自动格式化）、耗时、Trace ID。

支持按路由 ID、状态码、Trace ID、时间范围过滤。

### 生产环境的安全控制

日志功能很强大，但生产环境要注意安全。这个项目内置了一整套脱敏和采样机制：

```json
{
  "Gateway": {
    "Dashboard": {
      "EnableLogSampling": true,
      "LogSamplingRate": 0.1,
      "LogErrorsOnly": true,
      "LogHeaderBlacklist": ["Authorization", "Cookie", "Set-Cookie"],
      "LogJsonFieldSanitizeList": ["password", "token", "secret", "apikey"]
    }
  }
}
```

- `LogSamplingRate: 0.1` — 只记录 10% 的请求
- `LogErrorsOnly: true` — 只记录 4xx/5xx
- `LogHeaderBlacklist` — `Authorization` 等 Header 值替换为 `***REDACTED***`
- `LogJsonFieldSanitizeList` — JSON Body 里的 `password`、`token` 等字段自动脱敏

---

## 功能四：四种认证模式，按需选择

| 模式 | 适合场景 | 怎么用 |
|------|---------|--------|
| `None` | 本地开发 | 不设防，直接访问 |
| `DefaultJwt` | 个人/小团队 | 配个密码，用户名固定 `admin` |
| `CustomJwt` | 企业项目 | 自定义用户名 + 密码 |
| `ApiKey` | API 对接 | Header 传 API Key |

最简单的用法，appsettings.json 加三行：

```json
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "你的密码"
    }
  }
}
```

还有 `AuthorizeRequest` 委托，可以接你自己的认证体系（比如公司 SSO），优先级最高。

---

## 功能五：客户端零配置自动注册

微服务想自动注册到网关？客户端只需要一行代码：

```csharp
// 客户端 Program.cs
builder.Services.AddAneiangYarpClient();
```

配一下网关地址就行：

```json
{
  "Gateway": {
    "Registration": { "GatewayUrl": "http://网关地址:端口" }
  }
}
```

服务启动 → 自动注册；服务关闭 → 自动注销。

**更妙的是**：如果网关配了 Dashboard 认证，`AddGatewayApiAuth()` 会自动读取密码，客户端**完全不用配认证信息**：

```csharp
// 网关端
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpDashboard();
builder.Services.AddGatewayApiAuth();  // 自动读取 Dashboard 密码

// 客户端 — 不用配任何认证！
builder.Services.AddAneiangYarpClient();
```

---

## 功能六：多人开发不冲突

团队多人共用一个网关调试？Aneiang.Yarp 自动按机器名隔离路由：

| 开发者 | 路由名 | 匹配路径 |
|--------|--------|---------|
| 小明（PC-MING） | `my-service-PC-MING` | `/PC-MING/api/{**catch-all}` |
| 小红（PC-HONG） | `my-service-PC-HONG` | `/PC-HONG/api/{**catch-all}` |

各走各的，互不干扰。实例前缀转发时自动剥离，下游服务无感。

不需要可以关掉：`options.InstanceIsolation = false`。

---

## 生产环境推荐配置（直接抄）

```json
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "换成你自己的强密码",
      "EnableLogSampling": true,
      "LogSamplingRate": 0.1,
      "LogErrorsOnly": true,
      "LogMaxBodyLength": 4096,
      "LogHeaderBlacklist": ["Authorization", "Cookie", "Set-Cookie", "X-Api-Key"],
      "LogJsonFieldSanitizeList": ["password", "token", "secret", "apikey", "creditCard", "ssn"]
    }
  }
}
```

认证开了，采样开了（只记 10% 错误请求），Body 限制 4KB，敏感字段全脱敏。这个配置可以直接上生产。

---

## 完整配置一览

所有配置在 `Gateway:Dashboard` 下，**不配也能跑**：

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `EnableProxyLogging` | `true` | 日志总开关 |
| `RoutePrefix` | `"apigateway"` | 仪表盘 URL 前缀 |
| `Locale` | `"zh-CN"` | 默认语言，`zh-CN` / `en-US` |
| `AuthMode` | `None` | 认证模式 |
| `JwtPassword` | null | JWT 密码 |
| `JwtUsername` | null | CustomJwt 用户名 |
| `JwtSecret` | null | JWT 签名密钥，不配自动生成 |
| `ApiKey` | null | API Key 值 |
| `ApiKeyHeaderName` | `"X-Api-Key"` | API Key 的 Header 名 |
| `EnableLogSampling` | `false` | 启用采样 |
| `LogSamplingRate` | `1.0` | 采样率 0.0~1.0 |
| `LogErrorsOnly` | `false` | 只记错误 |
| `LogMaxBodyLength` | `8192` | Body 最大长度（字节） |
| `LogRouteWhitelist` | null | 路由白名单 |
| `LogRouteBlacklist` | null | 路由黑名单 |
| `LogHeaderBlacklist` | null | Header 脱敏列表 |
| `LogQueryBlacklist` | null | 查询参数脱敏列表 |
| `LogJsonFieldSanitizeList` | null | JSON 字段脱敏列表 |

---

## 项目信息

- **开源协议**：MIT（随便用）
- **支持框架**：.NET 8.0 / .NET 9.0
- **依赖**：YARP 2.3.0
- **项目地址**：[https://github.com/aneiang/Aneiang.Yarp](https://github.com/aneiang/Aneiang.Yarp)

```bash
dotnet add package Aneiang.Yarp.Dashboard  # 仪表盘
dotnet add package Aneiang.Yarp            # 核心库（可独立用）
```

---

## 总结

YARP 本身很优秀，但缺少管理界面一直是个遗憾。Aneiang.Yarp.Dashboard 很好地填补了这个空白：

- ✅ **真的简单** — 3 行代码，5 分钟装上
- ✅ **零侵入** — 不影响现有架构和配置
- ✅ **功能实用** — 配置回滚、日志脱敏、自动注册，都是刚需
- ✅ **MIT 协议** — 放心用

如果你正在用 YARP，不妨花 5 分钟试一下。

**觉得有用？去 GitHub 点个 Star 吧**：[https://github.com/aneiang/Aneiang.Yarp](https://github.com/aneiang/Aneiang.Yarp)

*（全文完）*
