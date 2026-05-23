# Aneiang.Yarp 源码解析系列

> 基于 YARP 2.3.0 的 .NET API 网关增强方案——从零到一拆解三大模块的架构设计与实现细节。

---

## 项目简介

[Aneiang.Yarp](https://github.com/aneiang/Aneiang.Yarp) 是一个开源的 YARP 网关增强方案，通过三个 NuGet 包解决实际使用中的四大痛点：

- **缺乏管理界面** → Dashboard 可视化面板
- **路由修改需重启** → 动态路由管理 + 配置持久化
- **缺少客户端自动注册** → 一行代码接入
- **多人开发调试冲突** → IP 隔离负载均衡

支持 .NET 8.0 / .NET 9.0，MIT 协议。

---

## 系列目录

| 篇 | 标题 | 关键词 |
|:--:|------|--------|
| **00** | [项目总览：YARP 没有管理界面？这个开源项目直接给你装上](./blog-introduction.zh-CN.md) | 功能全景、快速开始、Dashboard 预览 |
| **01** | [客户端自动注册：微服务接入网关只需一行代码](./blog-client.zh-CN.md) | 零 YARP 依赖、智能默认值、指数退避重试、心跳保活 |
| **02** | [网关核心模块：动态路由、配置持久化与审计日志](./blog-core.zh-CN.md) | InMemoryConfigProvider、ReaderWriterLockSlim、原子写入、环形缓冲区 |
| **03** | [可视化 Dashboard：嵌入式架构与实时日志采集](./blog-dashboard.zh-CN.md) | Razor Class Library、路由 Convention、快照回滚、日志脱敏 |
| **04** | [IP 隔离负载均衡：多人开发不再冲突](./blog-ip-isolation.zh-CN.md) | ILoadBalancingPolicy、零分配 IP 解析、精确注销 |

---

## 阅读建议

### 如果你想快速了解项目

从 **[篇 00](./blog-introduction.zh-CN.md)** 开始，5 分钟了解项目全貌。

### 如果你是微服务开发者

直接看 **[篇 01](./blog-client.zh-CN.md)**，一行代码让你的服务接入网关。

### 如果你对架构设计感兴趣

按顺序阅读 **篇 01 → 篇 02 → 篇 03**，从客户端到核心到面板，理解完整的数据流。

### 如果你正在做多人协作开发

重点看 **[篇 04](./blog-ip-isolation.zh-CN.md)**，IP 隔离负载均衡会改变你的调试体验。

---

## 代码仓库

- **GitHub**: [https://github.com/aneiang/Aneiang.Yarp](https://github.com/aneiang/Aneiang.Yarp)
- **在线 Demo**: http://113.45.65.71:8930/apigateway（admin / demo123）

## NuGet 包

| 包 | 用途 | 依赖 YARP |
|----|------|:---:|
| [`Aneiang.Yarp`](https://www.nuget.org/packages/Aneiang.Yarp) | 网关核心 | 是 |
| [`Aneiang.Yarp.Client`](https://www.nuget.org/packages/Aneiang.Yarp.Client) | 客户端自动注册 | 否 |
| [`Aneiang.Yarp.Dashboard`](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard) | 可视化管理面板 | 通过核心库 |
