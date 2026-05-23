# Aneiang.Yarp 源码解析系列（完整目录）

> **基于 YARP 2.3.0 的 .NET API 网关增强方案** —— 从项目总览到源码拆解，5 篇文章带你吃透三大模块的架构设计与实现细节。

## 项目简介

**Aneiang.Yarp** 是一个 MIT 协议开源的 YARP 网关增强方案，通过三个 NuGet 包解决 YARP 在实际使用中的四大痛点：

| 痛点 | 解决方案 | 对应模块 |
|------|---------|---------|
| YARP 缺乏管理界面 | Dashboard 可视化面板 | Aneiang.Yarp.Dashboard |
| 路由修改需重启 | 动态路由 + 配置持久化 | Aneiang.Yarp |
| 微服务接入需手动配置 | 一行代码自动注册 | Aneiang.Yarp.Client |
| 多人开发调试冲突 | IP 隔离负载均衡 | Aneiang.Yarp |

支持 **.NET 8.0 / .NET 9.0**，基于 **YARP 2.3.0**。

**在线体验**：http://113.45.65.71:8930/apigateway（admin / demo123）

---

## 系列目录

### 篇 00：项目总览与快速上手

**标题**：给 YARP 装上管理界面：一个开源的 .NET API 网关增强方案

**适合谁**：还没了解过项目的开发者，想快速判断是否适合自己。

**核心内容**：3 行代码跑起来、六大功能全景、Dashboard 截图预览、生产环境推荐配置。

👉 **阅读**：[blog-introduction.zh-CN.md](./blog-introduction.zh-CN.md)

---

### 篇 01：客户端自动注册模块

**标题**：微服务接入网关只需一行代码？Aneiang.Yarp.Client 源码解析

**适合谁**：微服务开发者，想了解自动注册的内部实现。

**核心内容**：零 YARP 依赖设计、智能默认值推断、localhost 自动解析为局域网 IP、指数退避重试、心跳保活、优雅关闭。

👉 **阅读**：[blog-client.zh-CN.md](./blog-client.zh-CN.md)

---

### 篇 02：网关核心模块

**标题**：YARP 动态路由管理怎么做？Aneiang.Yarp 核心模块架构深度解析

**适合谁**：对架构设计感兴趣，想学习配置双写、线程安全、审计日志的实现。

**核心内容**：InMemoryConfigProvider 双写机制、ReaderWriterLockSlim 并发控制、原子文件写入、环形缓冲区审计日志、API 鉴权三级凭证推断。

👉 **阅读**：[blog-core.zh-CN.md](./blog-core.zh-CN.md)

---

### 篇 03：可视化 Dashboard

**标题**：开箱即用的 YARP 管理面板：Aneiang.Yarp.Dashboard 架构与功能全解析

**适合谁**：想了解 Dashboard 是如何嵌入到 ASP.NET Core 中的，以及日志采集、快照回滚的实现。

**核心内容**：Razor Class Library 嵌入式架构、IApplicationModelConvention 路由注入、YARP 中间件日志采集管道、多层脱敏与采样、四种认证模式。

👉 **阅读**：[blog-dashboard.zh-CN.md](./blog-dashboard.zh-CN.md)

---

### 篇 04：IP 隔离负载均衡

**标题**：多人开发不再冲突：YARP 网关的 IP 隔离负载均衡方案

**适合谁**：团队协作开发者，深受"多实例调试冲突"困扰。

**核心内容**：传统方案 vs IP 隔离方案、ILoadBalancingPolicy 自定义实现、零分配 IP 解析（string.Create + ReadOnlySpan）、精确注销机制、Nginx 代理兼容。

👉 **阅读**：[blog-ip-isolation.zh-CN.md](./blog-ip-isolation.zh-CN.md)

---

## NuGet 包

| 包 | 用途 | 依赖 YARP |
|----|------|:---:|
| [`Aneiang.Yarp`](https://www.nuget.org/packages/Aneiang.Yarp) | 网关核心：动态路由、配置持久化、IP 隔离 | 是 |
| [`Aneiang.Yarp.Client`](https://www.nuget.org/packages/Aneiang.Yarp.Client) | 客户端自动注册（轻量，无 YARP 依赖） | 否 |
| [`Aneiang.Yarp.Dashboard`](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard) | 可视化管理面板：集群/路由 CRUD、日志、回滚 | 通过核心库 |

## 代码仓库

- **GitHub**：[https://github.com/AneiangSoft/Aneiang.Yarp](https://github.com/AneiangSoft/Aneiang.Yarp)
- **Gitee**：[https://gitee.com/aneiangsoft/aneiang-yarp](https://gitee.com/aneiangsoft/aneiang-yarp)
- **开源协议**：MIT

---

> 本文是 **Aneiang.Yarp 源码解析系列** 的索引页。系列文章同步发布于微信公众号、CSDN、博客园。
>
> 觉得有用？去 [GitHub](https://github.com/AneiangSoft/Aneiang.Yarp) 或 [Gitee](https://gitee.com/aneiangsoft/aneiang-yarp) 点个 Star 支持一下。
