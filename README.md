<div align="center">
<img src="Logo.png" alt="LOGO" width="240" style="border-radius: 15px;"/>

**Aneiang.Yarp — Full-featured API Gateway powered by YARP**

Dashboard · Dynamic Routing · WAF · AI Assistant · 2FA · Notifications · IP Isolation · Auto-Registration

[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp)
[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard)
[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Client.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Client)
[![YARP](https://img.shields.io/badge/YARP-2.3.0-blue.svg)](https://github.com/microsoft/reverse-proxy)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-purple.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

[English](README.md) | [中文](README.zh-CN.md)

</div>

---

**Aneiang.Yarp** is an enhanced, production-ready API gateway built on [Microsoft YARP](https://microsoft.github.io/reverse-proxy/) 2.3.0. It adds everything YARP leaves for you to build: a visual management dashboard, WAF firewall, AI assistant, notification alerts, health monitoring, circuit breaker views, client auto-registration, IP-based load balancing — all through three NuGet packages.

> **Docs**：https://yarp.aneiang.com

> **Live Demo**: https://yarp-test.aneiang.com/aneiang &nbsp;·&nbsp; `admin` / `demo123`

> **Online proxy address (site and proxy port are isolated)**: https://yarp-proxy.aneiang.com

---

## Packages

| Package | Purpose | Depends on YARP |
|:--------|:--------|:---:|
| **Aneiang.Yarp** | Gateway core: dynamic routing, config persistence, API auth, IP-based LB | ✅ |
| **Aneiang.Yarp.Dashboard** | Web admin UI: full CRUD, WAF, AI assistant (Function Calling), 2FA, notifications, health check, circuit breakers, audit log | via core |
| **Aneiang.Yarp.Client** | Client SDK: one-liner auto-register / unregister on startup and shutdown | ❌ |
| **Aneiang.Yarp.Storage.Abstractions** | Storage interfaces & entities (8 repository interfaces) | ❌ |
| **Aneiang.Yarp.Storage.Sqlite** | SQLite implementation with SQLCipher AES-256 encryption | via abstractions |
| **Aneiang.Yarp.Grpc** | gRPC registration protocol (GatewayRegistry.proto) | ❌ |

```
Aneiang.Yarp.Dashboard
  └── Aneiang.Yarp
        ├── Aneiang.Yarp.Client
        ├── Aneiang.Yarp.Grpc
        └── Aneiang.Yarp.Storage.Abstractions
              └── Aneiang.Yarp.Storage.Sqlite

Client microservice → reference Aneiang.Yarp.Client only (zero YARP SDK)
Gateway project     → reference Aneiang.Yarp + Aneiang.Yarp.Dashboard
```

---

## Dashboard

A full-featured web admin panel — **two lines of code** to enable:

```csharp
builder.Services.AddAneiangYarpDashboard();
// ...
app.UseAneiangYarpDashboard();
```

<p align="center">
  <img src="docs/overview.png" alt="Dashboard Overview" width="800"/>
</p>

### All 15 Dashboard Pages

| Group | Page | Description |
|:------|:-----|:------------|
| **Overview** | Overview | Active routes & clusters count, traffic QPS summary, cluster health quick preview, recent change timeline |
| **Gateway** | Clusters | Create, edit, delete clusters with destinations, configure active/passive health checks, `HttpRequest` & `HttpClient` settings |
| | Routes | Manage routes with transforms, metadata (`Waf:Enabled` per-route), drag-and-drop priority ordering, `CorsPolicy` config |
| **Monitoring** | Statistics | Request volume trends, P50/P90/P99 latency percentiles, HTTP status distribution pie chart, top routes ranking |
| | Logs | Real-time log stream (WebSocket) + history (TraceID-paired request/response merged view), filter by route/status/TraceID, sensitive data sanitization, sampling & errors-only mode |
| | Circuits | Per-cluster/destination circuit breaker status: Closed (green) / Open (red) / HalfOpen (yellow), consecutive failures vs threshold, recovery countdown, one-click reset |
| | Notifications | Manage webhook channels (DingTalk bot / generic HTTP), configure event rules (per-type toggle + cooldown), view notification history |
| | Health Check | Cluster-level health overview table, drill-down per destination showing real-time health status, last check time & result |
| **Security** | WAF Firewall | IP whitelist/blacklist (exact IP / CIDR / wildcard), SQL injection, XSS, path traversal detection, request size limits, security response headers |
| | Policies | Traffic policies: create, edit, enable, disable, reorder — supports retry, timeout, rate limit, request transform |
| | Plugins | View all registered `IGatewayPlugin` plugins, plugin metadata & load order |
| **System** | Config History | Auto-snapshot before every change, view full content of any snapshot, one-click rollback to any version |
| | Audit Log | Full audit trail: operation type, target, operator, before/after JSON diff, precise timestamp |
| | Deployment | Current deployment mode (AllInOne/Split/ProxyOnly/DashboardOnly), listening endpoints (name/address/port/role/public status), health check endpoints & security warnings |
| | Settings | Auth mode switching, JWT key config, AI config, log settings (sampling rate/sanitize list/body length limit), one-click database download, language switch |

---

## Core Features

### Dynamic Routing

YARP requires `appsettings.json` + restart for route changes. Aneiang.Yarp adds full runtime management:

- **Dual-source config**: static (`appsettings.json`) + dynamic (API / Dashboard), merged in YARP's `InMemoryConfigProvider`
- **Auto-persistence**: dynamic changes auto-save to `gateway-dynamic.json`, survive restarts
- **Source tracking**: every route & cluster records `CreatedAt`, `CreatedBy`, `Source` metadata
- **Thread-safe**: all operations protected by `SemaphoreSlim(1,1)` — async-friendly concurrency

### WAF Firewall

Production-grade web application firewall built into the gateway — no external service needed.

| Protection | Details |
|:-----------|:--------|
| **IP Blacklist / Whitelist** | Exact IP, CIDR (`192.168.1.0/24`), wildcard. Whitelist takes priority. Regex-cached for performance. |
| **SQL Injection** | Two precompiled regex patterns (keywords + injection values) with 5ms ReDoS timeout |
| **XSS** | Detects `<script>`, `javascript:`, event handler injection |
| **Path Traversal** | `../` variants, URL-encoded attacks, double-encoding |
| **Request Limits** | Max body size (default 10MB), max header count, max header size, URI length (4096) |
| **Security Headers** | Auto-inject `X-Content-Type-Options`, `X-Frame-Options`, `X-XSS-Protection`, `Referrer-Policy`, CSP |
| **Per-Route Control** | Enable/disable WAF per route via YARP metadata `Waf:Enabled` |

All WAF blocks are recorded in proxy logs, viewable in the Logs page with route and time filtering.

### Notification & Alerting

Multi-channel alerting for production incidents:

- **Webhook channels**: configure endpoints, timeout, retry counts
- **Event-based rules**: per-type toggle — circuit breaker open, retry exhausted, WAF block, proxy error, rate limit exceeded, config changes
- **Alert cooldown**: prevent duplicate alerts within cooldown window
- **Queue-based dispatch**: `ConfigChangeEventDispatcher` (BackgroundService) drains `ConcurrentQueue<PendingNotification>` at 200ms intervals
- **Notification history**: persisted to database, filterable by event type

### Health Check Monitoring

- Active & passive health check configuration per cluster
- Real-time health score and unhealthy destination drill-down
- `DefaultHealthCheckService`: auto-applys passive health checks at startup

### Circuit Breaker Panel

- Live status per cluster/destination: **Closed · Open · HalfOpen**
- Shows consecutive failures, threshold, recovery timeout countdown, opened-at time
- One-click reset all circuit breakers

### Client Auto-Registration

Microservices auto-register on startup, auto-unregister on shutdown — **truly one line**:

```csharp
builder.Services.AddAneiangYarpClient();
```

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://192.168.1.100:5000"
    }
  }
}
```

**Smart defaults** — only `GatewayUrl` is required:

| Option | Default | Description |
|:-------|:--------|:------------|
| `RouteName` | Entry assembly name | e.g. `MyAuthService` |
| `ClusterName` | Same as RouteName | |
| `MatchPath` | `/{**catch-all}` | Match all paths |
| `DestinationAddress` | Kestrel bind address | Auto-detected, `localhost` → LAN IP |
| `Order` | `50` | Route priority |

**Protocols**: HTTP REST API + gRPC (auto port = HTTP port + 1, Kestrel HTTP/2 h2c)

**Auth**: Bearer token, Basic auth, API key

**Retry**: exponential backoff on startup registration failure (2s → 4s → 8s → 16s → 30s, up to 5 attempts)

> `Aneiang.Yarp.Client` has **zero YARP dependency** — only `Microsoft.AspNetCore.App`. Clean dependency footprint for microservices.

### IP Isolation Load Balancing

Unique feature for **multi-developer debugging**. Each developer runs the same service locally — the gateway routes by client IP. The frontend is completely unaware.

```
Dev A (192.168.1.10) → POST /api/user → Gateway → 192.168.1.10:5001
Dev B (192.168.1.20) → POST /api/user → Gateway → 192.168.1.20:5001
Other users          → POST /api/user → Gateway → First available
```

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://localhost:5000",
      "RouteName": "my-service",
      "MatchPath": "/api/my-service/{**catch-all}",
      "UseIpIsolation": true
    }
  }
}
```

Implementation: custom `IpBasedLoadBalancingPolicy` matches request source IP (via `X-Forwarded-For` or `RemoteIpAddress`) against destination metadata.

### Configuration Management

- **Import / Export**: one-click export full YARP config. Import with automatic validation, snapshot, and persistence
- **Snapshot & Rollback**: auto-snapshot before every config change, rollback to any point in one click
- **Audit Log**: full trail — operation type, target, operator, before/after JSON diff, timestamp
- **SQLCipher Encryption**: database files encrypted at rest with AES-256 (`Data Source=gateway-store.db;Password=xxx`)
- **Database Download**: one-click download SQLite file for local inspection with DB tools

### Request Logging

`YarpRequestCaptureMiddleware` captures request data before forwarding and response data on return:

- **Full capture**: HTTP method, full URL, request/response headers, request/response body, elapsed time, TraceId
- **Real-time push**: WebSocket endpoint pushes logs to Dashboard in real time — no manual refresh needed
- **History with TraceID pairing**: `ProxyRequest` and `ProxyResponse` are auto-paired by TraceID and displayed as a merged row, showing the complete request → forward → response data flow
- **Sanitization**: `LogHeaderBlacklist` masks sensitive headers (`Authorization`, `Cookie`, etc.), `LogJsonFieldSanitizeList` recursively masks JSON fields (`password`, `token`, etc.)
- **Sampling & filtering**: `LogSamplingRate` (0.0–1.0) for rate-based sampling; `LogErrorsOnly` for 4xx/5xx only; combined filtering by route name, status code, TraceID
- **Structured storage**: `YarpEventFormatter` → `StructuredLogService` → SQLite with batch write optimization

### AI Assistant

Dashboard-embedded AI chat assistant, powered by OpenAI-compatible LLMs, enabling natural language gateway management:

**Multi-provider support**: Works with OpenAI, DeepSeek, Qwen, and any OpenAI-compatible API — switch via `Provider` + `BaseUrl` + `ApiKey`

**40 Function Calling tools**:
- **Read tools (21)**: auto-executed — query routes, clusters, circuit breaker status, proxy logs, traffic stats, WAF config, policies, audit log, etc.
- **Write tools (19)**: require user confirmation — create/delete routes, create/update clusters, reset circuit breakers, update WAF settings, rollback config, etc.

**Read/write separation security model**:
- Read operations execute automatically and return results — no manual intervention needed
- Write operations are proposed by AI, rendered as confirmation cards in the frontend, and only executed after the user clicks "Confirm"
- All write operations must be explicitly authorized by the user, ensuring operational safety

**Streaming & multi-turn conversation**:
- Direct streaming output (typewriter effect) when no tool call is needed; two-phase output (tool execution → streaming summary) when tools are involved
- Conversation history persisted to storage, supporting continuous context-aware dialogue
- AI response language auto-follows Dashboard UI language (Chinese / English)

**Floating chat on all pages**: a floating button at the bottom-right corner of every Dashboard page — one click to open the chat panel without leaving the current page

### Authentication

| Mode | Description |
|:-----|:------------|
| `None` | No auth (default) |
| `DefaultJwt` | Built-in JWT login (`admin` + password), secret auto-generated |
| `CustomJwt` | Custom username / password / secret |
| `ApiKey` | Via `X-Api-Key` header |
| Custom delegate | `AuthorizeRequest` — plug into your own auth system |

Dashboard API auth for client auto-registration: auto-infers credentials from Dashboard JWT config. No duplicate setup.

### Two-Factor Authentication (2FA)

TOTP-based 2FA for enhanced login security:

- **TOTP helper**: `TotpHelper` generates Base32 secrets + `otpauth://` URIs compatible with Google Authenticator, Microsoft Authenticator, etc.
- **Setup flow**: Settings page → generate secret → scan QR code → enter 6-digit code to verify binding
- **Login flow**: when 2FA enabled, login returns `202` → login page shows verification code input → submit code to complete login
- **Runtime persistence**: 2FA state saved to `twofactor-state.json`, survives restarts
- **Per-user control**: enable/disable 2FA anytime from Settings page

### Enterprise UI

- **Login page**: glassmorphism card, brand gradient panel, animated bubble background, version + license footer
- **Sidebar**: collapsible grouped menu (5 groups), auto-expand active group, brand header with gradient icon, user card with online indicator
- **Responsive**: mobile-friendly sidebar overlay, adaptive layouts
- **i18n**: Chinese / English runtime switch, 180+ i18n keys
- **Performance**: WOFF2 fonts, Brotli compression, per-page module loading, lazy Monaco editor, stripped unused language packs

---

## Quick Start

### 1. Create the Gateway

```bash
dotnet new web -n MyGateway
cd MyGateway
dotnet add package Aneiang.Yarp
dotnet add package Aneiang.Yarp.Dashboard
```

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpDashboard();

var app = builder.Build();

app.UseRouting();
app.UseAneiangYarpDashboard();  // includes MapReverseProxy
app.MapControllers();
app.Run();
```

Dashboard available at `/apigateway`.

### 2. Create a Microservice

```bash
dotnet new web -n MyService
cd MyService
dotnet add package Aneiang.Yarp.Client
```

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAneiangYarpClient();   // ← auto-registers on startup
builder.Services.AddControllers();

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.Run();
```

```json
// appsettings.json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://localhost:5000"
    }
  }
}
```

The microservice registers itself on startup and unregisters on shutdown. No extra code.

---

## Configuration Reference

All settings under `Gateway:*` — **everything has a default**, zero-config startup works.

```json
{
  "Gateway": {
    "Dashboard": {
      "RoutePrefix": "apigateway",
      "Locale": "en-US",
      "EnableProxyLogging": true,

      "AuthMode": "DefaultJwt",
      "JwtPassword": "your-strong-password",
      "JwtSecret": "...",

      "EnableLogSampling": false,
      "LogSamplingRate": 1.0,
      "LogErrorsOnly": false,
      "LogMaxBodyLength": 8192,
      "LogHeaderBlacklist": ["Authorization", "Cookie", "Set-Cookie"],
      "LogJsonFieldSanitizeList": ["password", "token", "secret", "apikey", "api-key"],

      "AI": {
        "Enabled": true,
        "Provider": "openai",
        "ApiKey": "sk-...",
        "BaseUrl": "https://api.openai.com/v1",
        "ChatModel": "gpt-4o-mini"
      }
    },
    "Storage": {
      "Sqlite": {
        "ConnectionString": "Data Source=gateway-store.db;Password=your-password"
      }
    }
  }
}
```

### Dashboard Options

| Option | Default | Description |
|:-------|:--------|:------------|
| `RoutePrefix` | `"apigateway"` | URL prefix for dashboard pages |
| `EnableProxyLogging` | `true` | Master switch for request logging |
| `Locale` | `"en-US"` | Default language (`en-US` / `zh-CN`), switchable at runtime |
| `AuthMode` | `None` | `None` / `ApiKey` / `CustomJwt` / `DefaultJwt` |
| `JwtPassword` | — | JWT login password |
| `JwtUsername` | — | Username (CustomJwt only; DefaultJwt fixed `admin`) |
| `JwtSecret` | auto-gen | JWT signing key; regenerate on restart if not configured |
| `ApiKey` | — | API Key value for `ApiKey` auth mode |
| `EnableLogSampling` | `false` | Enable rate-based log sampling |
| `LogSamplingRate` | `1.0` | Sampling rate 0.0–1.0 |
| `LogErrorsOnly` | `false` | Log only 4xx/5xx responses |
| `LogMaxBodyLength` | `8192` | Max captured body length in bytes |
| `LogHeaderBlacklist` | — | Headers to sanitize in logs |
| `LogJsonFieldSanitizeList` | — | JSON fields to sanitize in logs |

### Storage Options

| Option | Default | Description |
|:-------|:--------|:------------|
| `ConnectionString` | `Data Source=gateway-store.db` | SQLite connection string. Add `Password=xxx` for SQLCipher AES-256 encryption. |

### WAF Options

| Option | Default | Description |
|:-------|:--------|:------------|
| `Enabled` | `false` | WAF master switch |
| `IpWhitelist` | — | IP whitelist (per-line: IP or CIDR) |
| `IpBlacklist` | — | IP blacklist (per-line: IP or CIDR) |
| `MaxRequestBodySize` | `10485760` | Max request body size in bytes |
| `MaxHeaderCount` | `100` | Max number of request headers |
| `MaxHeaderSize` | `8192` | Max single header size in bytes |
| `EnableSqlInjectionDetection` | `true` | Detect SQL injection patterns |
| `EnableXssDetection` | `true` | Detect XSS patterns |
| `EnablePathTraversalDetection` | `true` | Detect path traversal attempts |
| `EnableIpCheck` | `true` | Enable IP whitelist/blacklist enforcement |
| `EnableRequestSizeValidation` | `true` | Enforce request body size limit |

### AI Options

Configuration under `Gateway:Dashboard:AI`:

| Option | Default | Description |
|:-------|:--------|:------------|
| `Enabled` | `false` | AI module master switch |
| `Provider` | `"openai"` | Provider: `openai` / `deepseek` / `qwen` / `custom` |
| `ApiKey` | — | LLM provider API key |
| `BaseUrl` | `"https://api.openai.com/v1"` | OpenAI-compatible API endpoint |
| `ChatModel` | `"gpt-4o-mini"` | Model used for interactive chat |
| `AnalysisModel` | `"gpt-4o-mini"` | Model used for background analysis (can use a cheaper model) |
| `MaxTokens` | `4096` | Max tokens per response |
| `Temperature` | `0.3` | Sampling temperature (0.0 = deterministic, 1.0 = creative) |
| `MaxConversationHistory` | `20` | Recent conversation turns to retain |
| `EnableBackgroundAnalysis` | `false` | Enable periodic background log analysis |
| `AnalysisInterval` | `01:00:00` | Background analysis interval |

---

## Advanced

<details>
<summary><b>Custom Authorization — Plug Into Your Own Auth System</b></summary>

```csharp
builder.Services.AddAneiangYarpDashboard(options =>
{
    options.AuthorizeRequest = async (context) =>
    {
        return context.User.Identity?.IsAuthenticated == true
            && context.User.IsInRole("GatewayAdmin");
    };
});
```

Priority: `AuthorizeRequest` > `ApiKey` > `JWT` > `None`

</details>

<details>
<summary><b>Gateway API Auth — Smart Credential Inference</b></summary>

When the gateway has Dashboard JWT auth configured, gateway management API auth auto-reads it:

```csharp
// Gateway
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpDashboard();
builder.Services.AddGatewayApiAuth();  // Auto-reads Dashboard JWT password
```

No extra config needed on the client side.

</details>

<details>
<summary><b>Middleware Order</b></summary>

```csharp
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseRouting();
app.UseAneiangYarpDashboard();  // ← after UseRouting (includes MapReverseProxy)
app.MapControllers();
```

The middleware captures YARP proxy request/response data and auto-skips dashboard requests.

</details>

<details>
<summary><b>Production Recommended Config</b></summary>

```json
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "very-strong-password-here",
      "JwtSecret": "your-persisted-secret-key",
      "EnableLogSampling": true,
      "LogSamplingRate": 0.1,
      "LogErrorsOnly": true,
      "LogMaxBodyLength": 4096,
      "LogHeaderBlacklist": ["Authorization", "Cookie", "Set-Cookie", "X-Api-Key"],
      "LogJsonFieldSanitizeList": ["password", "token", "secret", "apikey", "creditCard", "ssn"],

      "AI": {
        "Enabled": true,
        "Provider": "deepseek",
        "ApiKey": "sk-...",
        "BaseUrl": "https://api.deepseek.com/v1",
        "ChatModel": "deepseek-chat",
        "AnalysisModel": "deepseek-chat"
      }
    },
    "Storage": {
      "Sqlite": {
        "ConnectionString": "Data Source=gateway-store.db;Password=your-production-password"
      }
    }
  }
}
```

</details>

---

## Sample Projects

```bash
# Start gateway (with Dashboard)
dotnet run --project samples/SampleGateway

# Start client (auto-registers to gateway)
dotnet run --project samples/SampleLocalService

# Test
curl http://localhost:5000/api/your-endpoint
```

Dashboard: `/apigateway` · Login: `admin` / `demo123`

---

## NuGet

| Package | Description | NuGet |
|:--------|:------------|:-----:|
| **Aneiang.Yarp** | Gateway core: dynamic routing, IP isolation, API auth | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp) |
| **Aneiang.Yarp.Client** | Client auto-registration (lightweight, no YARP dep) | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Client.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Client) |
| **Aneiang.Yarp.Dashboard** | Web admin UI with full management capabilities | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard) |
| **Aneiang.Yarp.Storage.Abstractions** | Storage interfaces & entities | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Storage.Abstractions.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Storage.Abstractions) |
| **Aneiang.Yarp.Storage.Sqlite** | SQLite storage with SQLCipher encryption | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Storage.Sqlite.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Storage.Sqlite) |
| **Aneiang.Yarp.Grpc** | gRPC registration protocol | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Grpc.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Grpc) |

**.NET 8.0 / 9.0** &nbsp;·&nbsp; **YARP 2.3.0**

---

## License

[MIT](LICENSE)

---
<div align="center" style="display: flex; justify-content: center; align-items: center; gap: 20px; 
            background: #f6f8fa; padding: 20px 30px; border-radius: 12px; 
            border: 1px solid #e1e4e8; box-shadow: 0 2px 8px rgba(0,0,0,0.05);">
  <img src="docs/wechat_qrcode.jpg" alt="Official Account QR Code" 
       style="width: 140px; height: 140px; border-radius: 8px; border: 2px solid #d0d7de;" />
  <div style="text-align: left;">
    <h3 style="margin: 0 0 4px 0; font-size: 22px; font-weight: 600; color: #24292e;">
      递归不爆炸
    </h3>
    <p style="margin: 0; font-size: 16px; color: #586069; max-width: 280px;">
      Scan to follow and get more great content
    </p>
    <!-- Optional: add a WeChat badge -->
    <p style="margin-top: 8px;">
      <img src="https://img.shields.io/badge/WeChat-递归不爆炸-brightgreen?style=flat-square&logo=wechat" alt="WeChat" />
    </p>
  </div>
</div>
<div align="center">

If this project helps you, [⭐ Star it](https://github.com/aneiang/Aneiang.Yarp) to support development

</div>
