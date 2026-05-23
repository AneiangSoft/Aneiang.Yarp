# 给 YARP 装上管理界面：一个开源的 .NET API 网关增强方案

> **Aneiang.Yarp 源码解析系列（篇 00）**
> | 上一篇 — | [下一篇：微服务接入网关只需一行代码](./blog-client.zh-CN.md) | [系列目录](./series-index.zh-CN.md) |

用 YARP 做网关的同学，一定遇到过这些场景：

- 加个路由要改 `appsettings.json`，改完要重启
- 改坏了想回滚，翻遍 git log 也找不到完整的历史
- 想看请求日志，发现 YARP 本身不提供
- 微服务接入网关，每次都要手动配路由和集群地址
- 多人开发同一个服务，各自起实例，路由冲突

今天推荐一个开源项目 **Aneiang.Yarp**，专门解决这些痛点。

## 它是什么

[Aneiang.Yarp](https://gitee.com/aneiangsoft/aneiang-yarp) 是基于微软官方 [YARP (Yet Another Reverse Proxy)](https://microsoft.github.io/reverse-proxy/) 2.3.0 构建的 API 网关增强方案，MIT 协议开源，支持 .NET 8.0 和 .NET 9.0。

它通过三个 NuGet 包，提供 **Dashboard 管理面板 + 动态路由 + 自动注册 + IP 隔离** 四大能力：

| 包 | 用途 | 依赖 YARP |
|----|------|:---:|
| `Aneiang.Yarp` | 网关核心 | 是 |
| `Aneiang.Yarp.Client` | 客户端自动注册 | **否** |
| `Aneiang.Yarp.Dashboard` | 可视化管理面板 | 通过核心库 |

> **关键设计**：客户端包 `Aneiang.Yarp.Client` **不依赖 YARP SDK**，只依赖 `Microsoft.AspNetCore.App` 框架引用。下游微服务引用干净，不会间接拉入 YARP。

**在线体验**：http://113.45.65.71:8930/apigateway（admin / demo123）

---

## 3 行代码，跑起来

不需要数据库，不需要配前端，NuGet 装完就有管理后台。

**第一步，装包：**

```bash
dotnet add package Aneiang.Yarp
dotnet add package Aneiang.Yarp.Dashboard
```

**第二步，Program.cs 加两行：**

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAneiangYarp();          // 启用网关
builder.Services.AddAneiangYarpDashboard(); // 启用 Dashboard

var app = builder.Build();
app.UseRouting();
app.UseAneiangYarpDashboard(); // 注册中间件
app.MapControllers();
app.MapReverseProxy();         // 必须放最后
app.Run();
```

**第三步，打开浏览器：**

访问 `http://localhost:5000/apigateway`，管理后台已经在那了。

不配也能跑，所有配置都有默认值。你现有的 YARP 配置（`appsettings.json` 中的 `ReverseProxy` 节）完全不受影响。

---

## 六大功能一览

### 功能一：集群 & 路由管理

再也不用手改 JSON。Dashboard 提供表单创建和 JSON 编辑器双模式：

- **表单创建** — 填几个字段就建好路由或集群
- **JSON 编辑器** — 语法高亮 + 实时校验 + 自动补全，YARP 官方配置直接粘贴
- **智能关联** — 目标集群不存在？自动帮你创建
- **安全删除** — 删路由时清理孤立集群，删集群时更新引用路由

### 功能二：配置版本管理 — 改坏了一键回滚

每次变更前自动保存完整配置快照，记录时间、操作人 IP、变更内容。

改坏了？打开变更历史，选一个版本，点一下回滚。不用慌，不用翻 git log。

还支持配置导入导出——导出的是标准 YARP 格式，可以直接贴进 `appsettings.json`。

### 功能三：实时请求日志

经过网关的每个请求都能看到：方法、路径、请求/响应头、Body、耗时、Trace ID。

支持按路由、状态码、Trace ID 过滤。内置完整的**脱敏和采样机制**（后面会给生产环境推荐配置）。

### 功能四：四种认证模式

| 模式 | 适合场景 | 配置复杂度 |
|------|---------|-----------|
| `None` | 本地开发 | 零配置 |
| `DefaultJwt` | 个人 / 小团队 | 配一个密码 |
| `CustomJwt` | 企业项目 | 自定义用户名 + 密码 |
| `ApiKey` | API 对接 / 脚本调用 | 配一个 API Key |

最简单的用法，`appsettings.json` 加三行：

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

还支持 `AuthorizeRequest` 委托接入你自己的认证体系（如公司 SSO）。

### 功能五：微服务一行代码自动注册

```csharp
// 客户端 Program.cs — 就这一行
builder.Services.AddAneiangYarpClient();
```

配一下网关地址就行。服务启动 → 自动注册，服务关闭 → 自动注销。

更妙的是：网关端调了 `AddGatewayApiAuth()` 后，客户端**完全不用配认证信息**，密码自动推断。

### 功能六：多人开发不冲突 — IP 隔离

团队多人共用一个网关调试？开启 IP 隔离后，所有开发者使用**同一个路由路径**，网关按请求来源 IP 自动路由到对应实例：

```
开发者 A 的浏览器 → POST /api/user → 网关 → A 的本地实例
开发者 B 的浏览器 → POST /api/user → 网关 → B 的本地实例
```

前端完全无感，不需要改任何代码。

---

## 生产环境推荐配置

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
      "LogJsonFieldSanitizeList": ["password", "token", "secret", "apikey"]
    }
  }
}
```

认证开了、采样开了（只记 10% 错误请求）、Body 限制 4KB、敏感字段全脱敏。这个配置可以直接上生产。

> 更多的配置项（`JwtSecret`、`RoutePrefix`、`Locale`、路由黑白名单等）请参考项目 README。

---

## 完整示例

项目自带两个示例，`samples/` 目录下：

**网关（SampleGateway）：**

```csharp
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpDashboard();
// ...
app.UseAneiangYarpDashboard();
app.MapReverseProxy();
```

**微服务（SampleLocalService）：**

```csharp
builder.UseYarpKestrelAutoConfig();
builder.Services.AddAneiangYarpClient();
// 启动时自动注册到网关，关闭时自动注销
```

```bash
# 终端 1 — 启动网关
dotnet run --project samples/SampleGateway

# 终端 2 — 启动客户端服务
dotnet run --project samples/SampleLocalService

# 测试
curl http://localhost:5000/api/local-service/ping
```

Dashboard：`/apigateway`，登录：`admin / demo123`

---

## 总结

YARP 本身很优秀，但缺少管理界面和动态配置能力一直是实际使用的痛点。Aneiang.Yarp 填补了这个空白：

- 真的简单 — 3 行代码，5 分钟装上
- 零侵入 — 不影响现有架构和配置
- 功能实用 — 配置回滚、日志脱敏、自动注册，都是刚需
- MIT 协议 — 放心用

**源码地址**：[GitHub](https://github.com/AneiangSoft/Aneiang.Yarp) | [Gitee](https://gitee.com/aneiangsoft/aneiang-yarp)

如果你正在用 YARP，不妨花 5 分钟试一下。

---

> **Aneiang.Yarp 源码解析系列**
>
> | 上一篇 — | [下一篇：微服务接入网关只需一行代码](./blog-client.zh-CN.md) | [系列目录](./series-index.zh-CN.md) |
>
> 觉得有用？去 [GitHub](https://github.com/AneiangSoft/Aneiang.Yarp) 或 [Gitee](https://gitee.com/aneiangsoft/aneiang-yarp) 点个 Star 支持一下。
