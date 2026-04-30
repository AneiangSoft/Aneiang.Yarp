# Aneiang.Yarp 推广文档

## 文章标题（可选）

**主标题：**
- 一行代码搞定 .NET 动态网关！Aneiang.Yarp 让微服务路由管理如此简单
- 告别繁琐配置！基于 YARP 的 .NET 动态网关管理库来了
- .NET 开发者必备：Aneiang.Yarp —— 让网关管理像喝水一样简单

**副标题：**
- 基于 Microsoft YARP，提供动态路由、自动注册、可插拔监控仪表盘
- 核心库与仪表盘完全独立，按需组合，灵活使用
- 支持 .NET 8.0 / 9.0，开箱即用的网关管理方案

---

## 正文内容

### 引言

在微服务架构中，网关是每个项目都绕不开的基础设施。传统的网关配置方式往往需要在配置文件里写死路由规则，每次新增服务都要修改配置、重启网关，开发调试效率低下。

今天给大家介绍一个开源项目 **Aneiang.Yarp**，它基于 Microsoft 官方的 YARP（Yet Another Reverse Proxy）反向代理库，提供了一套**模块化、可插拔的动态网关管理方案**。整个项目由两个独立的 NuGet 包组成，你可以按需使用：

- **Aneiang.Yarp**（核心库）：提供动态路由管理 + 自动注册客户端，可独立使用
- **Aneiang.Yarp.Dashboard**（仪表盘）：提供监控运维界面，可选安装

无论你需要一个轻量级的动态网关，还是希望拥有完整的可视化监控能力，Aneiang.Yarp 都能满足你的需求。

---

### 📦 项目架构：两个独立的 NuGet 包

Aneiang.Yarp 采用**模块化设计**，核心功能与仪表盘完全解耦：

```
┌─────────────────────────────────────────────────┐
│           Aneiang.Yarp.Dashboard                 │
│      （可选安装：监控运维界面）                    │
│  • 集群/路由可视化查看                            │
│  • 实时请求日志捕获                               │
│  • JWT 登录认证                                   │
└─────────────────────────────────────────────────┘
                        ▲
                        │ 可选依赖
                        │
┌─────────────────────────────────────────────────┐
│              Aneiang.Yarp (核心库)               │
│         （独立使用：动态网关核心能力）             │
│  • 动态路由 API                                  │
│  • 自动注册客户端                                 │
│  • YARP 反向代理增强                              │
└─────────────────────────────────────────────────┘
```

**核心设计理念：**
- ✅ **Aneiang.Yarp 可独立使用**：不依赖 Dashboard 也能完整运行
- ✅ **Dashboard 是可选插件**：安装后即插即用，不安装不影响核心功能
- ✅ **按需组合**：根据你的场景自由选择

---

### 🎯 核心库：Aneiang.Yarp

**Aneiang.Yarp** 是项目的核心库，提供完整的动态网关管理能力，**可以完全不依赖 Dashboard 独立运行**。

#### 特性 1：动态路由 API

提供 RESTful API，支持运行时动态注册、更新、删除路由：

```bash
POST /api/gateway/register-route    # 注册路由
PUT  /api/gateway/{routeName}       # 更新路由
DELETE /api/gateway/{routeName}     # 删除路由
GET  /api/gateway/routes            # 查询所有路由
```

**使用场景：**
- 通过 API 动态管理路由，无需修改配置文件
- 配合 CI/CD 流水线实现自动化部署
- 构建自定义的网关管理界面

#### 特性 2：一行代码，自动注册

微服务只需在 `Program.cs` 中添加一行代码：

```csharp
builder.Services.AddAneiangYarpClient();
```

服务启动时会自动注册到网关，关闭时自动注销。无需手动配置路由，无需重启网关！

> **适用场景**：开发调试、内网服务协同、快速原型开发
> 
> **注意**：自动注册需要客户端服务与网关之间**网络互通**，主要为**开发和调试场景**设计。

**自动注册的权限设置（三种场景）：**

| 场景 | 网关配置 | 客户端配置 | 说明 |
|------|----------|------------|------|
| **场景 1：单独使用 Aneiang.Yarp** | 设置 API 认证（BasicAuth/ApiKey） | 手动配置认证凭据 | 需要显式配置网关 API 的认证信息 |
| **场景 2：使用 Dashboard（已设置权限）** | Dashboard 配置了 JWT/ApiKey 认证 | 自动从 Dashboard 配置读取凭据 | 客户端会自动读取 `Gateway:Dashboard` 配置，无需额外配置 |
| **场景 3：使用 Dashboard（未设置权限）** | Dashboard 未配置认证 | 无需配置 | 无认证保护，适合本地开发环境 |

**场景 1 示例：单独使用 Aneiang.Yarp**

```csharp
// 网关端：配置 API 认证
builder.Services.AddGatewayApiAuth(o =>
{
    o.Mode = GatewayApiAuthMode.ApiKey;
    o.ApiKey = "your-secret-api-key";
});

// 客户端：手动配置认证凭据
builder.Services.AddAneiangYarpClient(o =>
{
    o.GatewayUrl = "http://gateway:5000";
    o.ApiAuthMode = GatewayApiAuthMode.ApiKey;
    o.ApiKey = "your-secret-api-key";
});
```

**场景 2 示例：使用 Dashboard（已设置权限）**

```csharp
// 网关端：Dashboard 已配置认证
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "your-strong-password"
    }
  }
}

// 客户端：无需额外配置，自动读取 Dashboard 的认证信息
builder.Services.AddAneiangYarpClient();
```

**场景 3 示例：使用 Dashboard（未设置权限）**

```csharp
// 网关端：Dashboard 未配置认证（仅本地开发）
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpDashboard();

// 客户端：直接注册，无需认证
builder.Services.AddAneiangYarpClient();
```

#### 特性 3：多人协作调试支持

内置**实例隔离**功能，自动为不同开发者的路由添加机器标识前缀，避免路由冲突：

| 开发者 | 路由名称 | 匹配路径 |
|--------|----------|----------|
| DevA | `my-service-PC-JOHN` | `/PC-JOHN/api/{**catch-all}` |
| DevB | `my-service-PC-JANE` | `/PC-JANE/api/{**catch-all}` |

无需额外配置，开箱即用！

#### 特性 4：智能默认值

- 自动检测程序集名称作为服务名
- 自动获取 Kestrel 监听地址
- localhost 自动解析为局域网 IP（方便其他机器访问）

#### 特性 5：多级网关链路

内网网关也可以注册到外网网关，形成多级代理：

```csharp
// 内网网关同时也是一个客户端
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpClient(o =>
{
    o.GatewayUrl = "http://outer-gateway:5000";
});
```

#### 特性 6：灵活的认证保护

为网关管理 API 添加认证：

```csharp
// 方式一：BasicAuth
builder.Services.AddGatewayApiAuth(o =>
{
    o.Mode = GatewayApiAuthMode.BasicAuth;
    o.Username = "admin";
    o.Password = "admin@2026";
});

// 方式二：ApiKey
builder.Services.AddGatewayApiAuth(o =>
{
    o.Mode = GatewayApiAuthMode.ApiKey;
    o.ApiKey = "your-secret-api-key";
});
```

---

### 🌟 可选插件：Aneiang.Yarp.Dashboard

**Aneiang.Yarp.Dashboard** 是主推产品，提供了一个功能完善的监控运维界面。**它是可选的，安装后即插即用，不影响核心库的独立运行。**

#### 特性 1：集群状态总览

实时查看所有服务集群的运行状态、健康检查信息、负载均衡策略等。

#### 特性 2：路由配置管理

可视化查看路由规则，支持展开查看完整配置详情（Transforms、Headers、Metadata 等）。

#### 特性 3：实时日志监控

捕获 YARP 转发日志和请求/响应详情，支持：
- 按类别过滤（YARP 事件日志 / 请求日志）
- 展开查看完整堆栈
- 自动刷新 / 手动刷新

#### 特性 4：多模式认证

支持多种认证方式保护管理界面：
- **JWT 登录**：提供登录页面，支持 Cookie 和 Bearer Token
- **API Key**：适合 API 调用场景
- **自定义委托**：完全自定义认证逻辑

---

---

### 🚀 快速开始

#### 场景一：仅使用核心库（不安装 Dashboard）

如果你只需要动态路由和自动注册功能，不需要可视化界面：

**Step 1：安装 NuGet 包**

```bash
dotnet add package Aneiang.Yarp
```

**Step 2：修改 Program.cs**

```csharp
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 一行代码启用网关（不依赖 Dashboard）
builder.Services.AddAneiangYarp();

var app = builder.Build();
app.MapReverseProxy();
app.Run();
```

**Step 3（可选）：配置 API 认证**

```json
{
  "Gateway": {
    "ApiAuth": {
      "Mode": "ApiKey",
      "ApiKey": "your-secret-api-key"
    }
  }
}
```

完成！网关已启动，你可以通过 REST API 管理路由，或让微服务自动注册。

---

#### 场景二：核心库 + Dashboard（完整方案）

如果你需要可视化监控界面：

**Step 1：安装 NuGet 包**

```bash
dotnet add package Aneiang.Yarp
dotnet add package Aneiang.Yarp.Dashboard
```

**Step 2：修改 Program.cs**

```csharp
using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 启用网关核心功能
builder.Services.AddAneiangYarp();

// 启用监控仪表盘（可选）
builder.Services.AddAneiangYarpDashboard();

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.MapReverseProxy();
app.Run();
```

**Step 3：配置 Dashboard 认证**

```json
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "your-strong-password"
    }
  }
}
```

完成！访问 `http://localhost:5000/apigateway` 即可看到监控仪表盘。

---

#### 接入微服务（同样只需一行代码）

在你的微服务项目中：

```csharp
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 一行代码：启动自动注册 → 关闭自动注销
builder.Services.AddAneiangYarpClient();

builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
```

配置文件指定网关地址：

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://192.168.1.100:5000"
    }
  }
}
```

搞定！服务启动后会自动在网关中注册路由。

---

### 📸 Dashboard 界面展示

> 以下截图展示了 Aneiang.Yarp.Dashboard 的功能界面。如果你仅使用核心库，不需要安装 Dashboard。

#### 集群状态总览

![Cluster List](docs/cluster-list.png)

#### 路由配置管理

![Route List](docs/route-list.png)

#### 实时日志监控

![Log List](docs/log-list.png)

---

### 💡 高级用法

#### 🔐 AddGatewayApiAuth 详解：保护网关管理 API

`AddGatewayApiAuth` 方法用于为网关的动态路由管理 API（`/api/gateway/*`）添加认证保护，防止未授权的路由注册/删除操作。

**方法签名：**

```csharp
public static IServiceCollection AddGatewayApiAuth(
    this IServiceCollection services,
    Action<GatewayApiAuthOptions>? configureOptions = null)
```

**支持的认证模式：**

| 模式 | 枚举值 | 说明 | 适用场景 |
|------|--------|------|----------|
| 无认证 | `GatewayApiAuthMode.None` | 不需要认证（默认） | 本地开发环境 |
| BasicAuth | `GatewayApiAuthMode.BasicAuth` | HTTP Basic 认证 | 简单的服务器间认证 |
| ApiKey | `GatewayApiAuthMode.ApiKey` | API Key 认证（Header 或 Query） | 生产环境推荐 |

---

**使用方式 1：从配置文件读取（推荐）**

```csharp
// Program.cs
builder.Services.AddGatewayApiAuth();
```

```json
// appsettings.json
{
  "Gateway": {
    "ApiAuth": {
      "Mode": "ApiKey",
      "ApiKey": "your-secret-api-key-2026",
      "ApiKeyHeaderName": "X-Api-Key"  // 可选，默认就是 X-Api-Key
    }
  }
}
```

---

**使用方式 2：代码中配置（最高优先级）**

```csharp
// BasicAuth 模式
builder.Services.AddGatewayApiAuth(o =>
{
    o.Mode = GatewayApiAuthMode.BasicAuth;
    o.Username = "admin";
    o.Password = "admin@2026";
});

// ApiKey 模式
builder.Services.AddGatewayApiAuth(o =>
{
    o.Mode = GatewayApiAuthMode.ApiKey;
    o.ApiKey = "your-secret-api-key-2026";
    o.ApiKeyHeaderName = "X-Custom-ApiKey";  // 自定义 Header 名称
});
```

> **注意**：代码配置的优先级最高，会覆盖配置文件中的设置。

---

**使用方式 3：自动从 Dashboard 配置读取（智能检测）**

如果你已经配置了 Dashboard 的认证，`AddGatewayApiAuth()` 会自动读取 Dashboard 的配置：

```json
// appsettings.json
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "your-strong-password"
    }
  }
}
```

```csharp
// Program.cs - 无需任何配置，自动检测
builder.Services.AddGatewayApiAuth();

// 系统会自动使用：
// - Mode = BasicAuth
// - Username = "admin"
// - Password = "your-strong-password" (从 JwtPassword 读取)
```

> **适用场景**：使用 Dashboard 且已设置权限时，客户端自动注册无需额外配置。

---

**配置优先级（从高到低）：**

```
1. 代码回调配置 (configureOptions 参数)
      ↓ 覆盖
2. Gateway:ApiAuth 配置节
      ↓ 覆盖
3. Gateway:Dashboard 自动检测
```

---

**客户端如何调用受保护的 API？**

当网关启用了 API 认证后，客户端服务在自动注册时需要配置认证凭据：

```csharp
// 场景 1：网关使用 BasicAuth
builder.Services.AddAneiangYarpClient(o =>
{
    o.GatewayUrl = "http://gateway:5000";
    o.ApiAuthMode = GatewayApiAuthMode.BasicAuth;
    o.Username = "admin";
    o.Password = "admin@2026";
});

// 场景 2：网关使用 ApiKey
builder.Services.AddAneiangYarpClient(o =>
{
    o.GatewayUrl = "http://gateway:5000";
    o.ApiAuthMode = GatewayApiAuthMode.ApiKey;
    o.ApiKey = "your-secret-api-key-2026";
});

// 场景 3：网关使用 Dashboard 认证（自动读取）
builder.Services.AddAneiangYarpClient();  // 无需配置！
```

---

**手动调用 API 示例：**

如果你需要通过 Postman 或 curl 手动调用网关 API：

```bash
# BasicAuth 模式
curl -X POST http://gateway:5000/api/gateway/register-route \
  -u admin:admin@2026 \
  -H "Content-Type: application/json" \
  -d '{"routeName": "my-service", ...}'

# ApiKey 模式 (Header 方式)
curl -X POST http://gateway:5000/api/gateway/register-route \
  -H "X-Api-Key: your-secret-api-key-2026" \
  -H "Content-Type: application/json" \
  -d '{"routeName": "my-service", ...}'

# ApiKey 模式 (Query 方式)
curl -X POST "http://gateway:5000/api/gateway/register-route?api-key=your-secret-api-key-2026" \
  -H "Content-Type: application/json" \
  -d '{"routeName": "my-service", ...}'
```

---

**完整示例：生产环境推荐配置**

```csharp
// 网关端 Program.cs
var builder = WebApplication.CreateBuilder(args);

// 1. 启用网关核心功能
builder.Services.AddAneiangYarp();

// 2. 启用 Dashboard 并配置认证
builder.Services.AddAneiangYarpDashboard();

// 3. 启用 API 认证（自动读取 Dashboard 配置）
builder.Services.AddGatewayApiAuth();

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.MapReverseProxy();
app.Run();
```

```json
// 网关端 appsettings.json
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "YourStrongPassword@2026!"
    },
    // ApiAuth 可以省略，会自动从 Dashboard 读取
  }
}
```

```csharp
// 客户端 Program.cs
builder.Services.AddAneiangYarpClient();  // 自动读取网关配置，无需额外设置
```

---

#### 自定义路由转换

支持 YARP 的所有 Transform 能力：

```json
{
  "Gateway": {
    "Registration": {
      "Transforms": [
        { "PathRemovePrefix": "/api" },
        { "PathPrefix": "/backend" },
        { "RequestHeader": "X-Custom-Header", "Set": "value" }
      ]
    }
  }
}
```

---

### 📦 NuGet 包

| 包名 | 说明 | 是否独立 | 链接 |
|------|------|----------|------|
| **Aneiang.Yarp** | 核心库：动态路由 + 自动注册客户端 | ✅ 可独立使用 | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp) |
| **Aneiang.Yarp.Dashboard** | 🌟 **主推产品**：监控运维仪表盘 | ❌ 依赖核心库 | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard) |

---

### 🤔 常见问题

#### Q1：Aneiang.Yarp 和 Aneiang.Yarp.Dashboard 是什么关系？

A：**Aneiang.Yarp 是核心库**，提供动态路由和自动注册功能，**可以完全独立运行**。**Aneiang.Yarp.Dashboard 是可选的监控界面**，安装后即插即用，不安装也不影响核心功能。你可以根据需求自由选择。

#### Q2：自动注册功能需要什么条件？

A：需要客户端服务与网关之间**网络互通**。此功能主要为**开发和调试场景**设计，生产环境建议手动配置路由或使用服务发现组件。

#### Q2.1：自动注册时，客户端如何配置认证？

A：根据网关的配置方式，有三种场景：

- **场景 1：单独使用 Aneiang.Yarp**
  - 网关配置了 API 认证（BasicAuth/ApiKey）
  - 客户端需要手动配置认证凭据
  - 示例：`o.ApiAuthMode = GatewayApiAuthMode.ApiKey; o.ApiKey = "your-key";`

- **场景 2：使用 Dashboard（已设置权限）**
  - Dashboard 配置了 JWT/ApiKey 认证
  - 客户端**自动读取** `Gateway:Dashboard` 配置，**无需额外配置**
  - 这是最推荐的方式，配置最少

- **场景 3：使用 Dashboard（未设置权限）**
  - Dashboard 未配置认证（仅本地开发）
  - 客户端无需配置，直接注册
  - 适合本地快速调试，生产环境不推荐

#### Q3：支持生产环境使用吗？

A：
- **Aneiang.Yarp**（核心库）：✅ 支持生产环境，提供完整的 YARP 反向代理能力
- **Aneiang.Yarp.Dashboard**（仪表盘）：✅ 可作为运维监控工具在生产环境使用
- **自动注册功能**：⚠️ 建议仅在开发调试环境使用

#### Q4：与 Ocelot 相比有什么优势？

A：
- 基于 Microsoft 官方 YARP，性能和兼容性更好
- 模块化设计，核心库与仪表盘解耦，按需使用
- 更简洁的 API 设计，学习成本低
- 内置自动注册和监控仪表盘，开箱即用
- 专为内网调试场景优化

#### Q5：如何自定义路由匹配规则？

A：支持 YARP 的所有配置选项，包括 Path、Hosts、Methods、Headers 等，完全兼容 YARP 官方文档。

---

### 🌟 为什么选择 Aneiang.Yarp？

1. **模块化设计**：核心库与仪表盘完全解耦，按需组合
2. **极简 API**：一行代码搞定网关搭建和服务注册
3. **零学习成本**：完全兼容 YARP，熟悉 YARP 就能上手
4. **开发友好**：自动注册 + 实例隔离，多人协作不冲突
5. **运维友好**：可选的实时监控仪表盘，问题排查一目了然
6. **高度可扩展**：保留 YARP 全部能力，支持自定义中间件、Transform、健康检查等

---

### 📚 资源链接

- **GitHub 仓库**：https://github.com/aneiang/Aneiang.Yarp
- **NuGet 包**：
  - https://www.nuget.org/packages/Aneiang.Yarp
  - https://www.nuget.org/packages/Aneiang.Yarp.Dashboard
- **文档**：README.md / README.zh-CN.md
- **示例项目**：samples/SampleGateway、samples/SampleLocalService

---

### 📝 许可证

MIT License — 免费用于商业和个人项目

---

### 💬 互动引导

如果你觉得这个项目对你有帮助，欢迎：

1. ⭐ **Star GitHub 仓库**：https://github.com/aneiang/Aneiang.Yarp
2. 📦 **安装 NuGet 包**试试效果
3. 🐛 **提交 Issue**：遇到问题或有新需求
4.  **提交 PR**：一起完善这个项目

**有问题或建议？欢迎在评论区留言！**

---

## 微信公众号排版建议

### 标题样式

```
【开源推荐】一行代码搞定 .NET 动态网关！Aneiang.Yarp 让微服务路由管理如此简单
```

### 文章结构

1. **开头**：痛点引入 + 模块化架构介绍（核心库 vs Dashboard）
2. **核心库特性**：动态路由 API + 自动注册 + 实例隔离
3. **Dashboard 特性**：监控界面展示（强调可选安装）
4. **使用场景**：场景一（仅核心库）vs 场景二（完整方案）
5. **快速开始**：分步骤，每步配截图或代码
6. **高级用法**：折叠展开或简要说明
7. **FAQ**：解答常见疑问（强调独立性）
8. **结尾**：引导 Star 和反馈

### 排版技巧

- 代码块使用微信公众号的代码格式
- 关键信息加粗或用引用框
- 截图添加边框和说明文字
- 使用分隔线区分章节
- 结尾添加公众号二维码和 Star 引导

---

## 博客园排版建议

### Markdown 格式

博客园支持 Markdown，可以直接使用本文档的格式。建议：

1. 使用 H2/H3 标题组织章节
2. 代码块指定语言（```csharp）
3. 表格展示配置对比
4. 图片使用相对路径或 CDN 链接
5. 添加标签：`.NET`、`YARP`、`网关`、`微服务`、`开源`

### SEO 优化

**关键词**：.NET 网关、YARP、动态路由、微服务、自动注册、开源项目

**摘要**：介绍 Aneiang.Yarp —— 基于 Microsoft YARP 的 .NET 动态网关管理库，提供一行代码自动注册、实时监控仪表盘、多人协作调试等特性，让网关管理变得前所未有的简单。

---

## 其他平台适配

### 掘金/知乎

- 使用 Markdown 格式
- 添加相关标签和话题
- 在文章开头添加个人简介和项目链接
- 结尾添加讨论引导

### CSDN

- 支持 Markdown 和富文本
- 添加分类：后端开发/.NET
- 添加关键词标签
- 可以在代码块后添加详细说明

---

**文档生成日期**：2026-04-29
**适用版本**：Aneiang.Yarp 2.3.0
