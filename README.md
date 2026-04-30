# Aneiang.Yarp

<div align="center">

**Dynamic Routing Gateway Management Library for .NET**

[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp)
[![License](https://img.shields.io/github/license/aneiang/Aneiang.Yarp.svg)](LICENSE)
[![YARP Version](https://img.shields.io/badge/YARP-2.3.0-blue.svg)](https://github.com/microsoft/reverse-proxy)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-purple.svg)](https://dotnet.microsoft.com/)

[English](README.md) | [中文](README.zh-CN.md)

</div>

---

A powerful **dynamic routing gateway management library** built on [Microsoft YARP](https://github.com/microsoft/reverse-proxy), providing runtime route registration, automatic service discovery, real-time monitoring dashboard, while preserving all YARP reverse proxy capabilities.

## 📦 Project Architecture: Two Independent NuGet Packages

Aneiang.Yarp uses a **modular design** with core functionality completely decoupled from the dashboard:

```
┌─────────────────────────────────────────────────┐
│           Aneiang.Yarp.Dashboard                 │
│      (Optional: Monitoring & Operations UI)      │
│  • Cluster/Route visualization                   │
│  • Real-time request log capture                 │
│  • JWT login authentication                      │
└─────────────────────────────────────────────────┘
                        ▲
                        │ Optional dependency
                        │
┌─────────────────────────────────────────────────┐
│              Aneiang.Yarp (Core Library)         │
│     (Independent: Dynamic Gateway Capabilities)  │
│  • Dynamic routing API                           │
│  • Auto-registration client                      │
│  • YARP reverse proxy enhancements               │
└─────────────────────────────────────────────────┘
```

**Core Design Principles:**
- ✅ **Aneiang.Yarp works independently**: Runs fully without Dashboard
- ✅ **Dashboard is an optional plugin**: Install and use, or skip it entirely
- ✅ **Flexible combination**: Choose based on your needs

---

## 🎯 Core Library: Aneiang.Yarp

**Aneiang.Yarp** is the core library providing complete dynamic gateway management capabilities. **It can run completely independently without Dashboard.**

### Features

| Feature | Description |
|---------|-------------|
| 🚀 **Dynamic Routing** | REST API for runtime route registration/update/unregistration |
| 🔄 **Auto-Registration** | Services auto-register on startup & unregister on shutdown — **1 line of code** |
| 👥 **Instance Isolation** | Automatic namespace isolation for multi-developer debugging |
| 🧠 **Smart Defaults** | Auto-detect assembly name, Kestrel address, resolve localhost to LAN IP |
| 🛡️ **API Authorization** | Optional BasicAuth/ApiKey protection for registration APIs |
| 🚪 **Conditional API Exposure** | Enable/disable registration API via `enableRegistration` parameter |

### Quick Start (Core Library Only)

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ⭐ One-line gateway setup (no Dashboard dependency)
builder.Services.AddAneiangYarp();

var app = builder.Build();
app.MapReverseProxy();
app.Run();
```

---

## 🌟 Optional Plugin: Aneiang.Yarp.Dashboard

**Aneiang.Yarp.Dashboard** is the **recommended product** providing a comprehensive monitoring and operations UI. **It's optional — install it for enhanced visibility, or skip it for lightweight deployments.**

### Features

| Feature | Description |
|---------|-------------|
| 📊 **Cluster Status** | Real-time view of all service clusters and health checks |
| 🛣️ **Route Management** | Visualize route rules with expandable configuration details |
| 📝 **Real-time Logs** | Capture YARP forwarding logs and request/response details |
| 🔐 **Multi-Mode Auth** | JWT login, API Key, or custom delegate authentication |
| 🌐 **i18n Support** | Runtime language switching: English / Chinese |

### Enable Dashboard

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Enable core gateway
builder.Services.AddAneiangYarp();

// Enable monitoring dashboard (optional)
builder.Services.AddAneiangYarpDashboard();

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.MapReverseProxy();
app.Run();
```

Access dashboard at: `http://localhost:5000/apigateway`
---

## 🚀 Client Service: Auto-Registration

> **Note**: Auto-registration requires network connectivity between the client service and gateway. This feature is primarily designed for **development and debugging scenarios**.

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ⭐ One line: auto-register on startup → auto-unregister on shutdown
builder.Services.AddAneiangYarpClient();

builder.Services.AddControllers();

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.Run();
```

**`appsettings.json`** (minimum config):

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://192.168.1.100:5000"
    }
  }
}
```

That's it! Your service will automatically register to the gateway on startup and unregister on shutdown.

---

## 📦 NuGet Packages

| Package | Description | Independent? | Link |
|---------|-------------|--------------|------|
| **Aneiang.Yarp** | Core library: dynamic routing + auto-registration client | ✅ Yes | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp) |
| **Aneiang.Yarp.Dashboard** | 🌟 **Recommended**: Dashboard for monitoring & operations | ❌ Requires core | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard) |

**Requirements:**
- Target Framework: `.NET 8.0` / `.NET 9.0`
- YARP Version: `2.3.0`

---

## 📸 Dashboard Screenshots

### Cluster Status

![Cluster List](docs/cluster-list.png)

### Route Configuration

![Route List](docs/route-list.png)

### Log Viewer

![Log List](docs/log-list.png)

---

## ⚡ Advanced Usage

<details>
<summary><b>🔗 Multi-Level Gateway Chain</b></summary>

```csharp
// Intranet gateway also registers to external gateway
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpClient(o =>
{
    o.GatewayUrl = "http://outer-gateway:5000";
});
```

</details>

<details>
<summary><b>🛡️ Gateway API Authorization (Optional)</b></summary>

> **Important**: `AddGatewayApiAuth()` is **optional**. Without it, registration APIs are publicly accessible.

**When to use:**
- ✅ **Call it**: Production environment, public network, need access control
- ❌ **Skip it**: Local development, isolated intranet (network-level protection)

```csharp
// Option 1: Auto-detect from Dashboard config (recommended)
builder.Services.AddGatewayApiAuth();

// Option 2: Explicit configuration
builder.Services.AddGatewayApiAuth(o =>
{
    o.Mode = GatewayApiAuthMode.BasicAuth;
    o.Username = "admin";
    o.Password = "admin@2026";
});
```

Config file:
```json
{
  "Gateway": {
    "ApiAuth": {
      "Mode": "BasicAuth",
      "Username": "admin",
      "Password": "admin@2026"
    }
  }
}
```

**Configuration Priority** (later overrides earlier):
1. `Gateway:ApiAuth` config section
2. Auto-detect from `Gateway:Dashboard` (if Dashboard JWT password configured)
3. `configureOptions` callback (highest precedence)

**Auto-Detection Logic:**
When `AddGatewayApiAuth()` is called without explicit configuration:
- If `Gateway:Dashboard:JwtPassword` exists → automatically uses BasicAuth with username `admin` and password from `JwtPassword`
- This enables zero-config client auto-registration when Dashboard auth is configured

</details>

<details>
<summary><b>🔐 Auto-Registration Authorization (3 Scenarios)</b></summary>

When clients auto-register to gateway, authentication can be configured in three ways:

| Scenario | Gateway Config | Client Config | Description |
|----------|---------------|---------------|-------------|
| **1: Aneiang.Yarp only** | Explicit API auth | Manual credentials | Requires explicit config |
| **2: Dashboard (auth enabled)** | Dashboard JWT/ApiKey | **Auto-read** from Dashboard config | **Zero-config** (recommended) |
| **3: Dashboard (no auth)** | No auth configured | No config needed | Local dev only |

**Scenario 2 Example (Recommended):**
```json
// Gateway appsettings.json
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
// Gateway Program.cs
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpDashboard();
builder.Services.AddGatewayApiAuth();  // Auto-reads Dashboard config

// Client Program.cs - ZERO configuration needed!
builder.Services.AddAneiangYarpClient();
```

</details>

<details>
<summary><b>👥 Instance Isolation (Multi-Developer)</b></summary>

**Enabled by default** — automatically embeds machine identifier into routes:

| Dimension | DevA (PC-JOHN) | DevB (PC-JANE) |
|-----------|----------------|----------------|
| routeName | `my-service-PC-JOHN` | `my-service-PC-JANE` |
| matchPath | `/PC-JOHN/api/{**catch-all}` | `/PC-JANE/api/{**catch-all}` |

**Special Handling:**
- Automatically detects machine name as instance ID
- Prevents route conflicts when multiple developers test against same gateway
- Instance prefix is stripped before forwarding to downstream service

Custom instance ID:
```csharp
builder.Services.AddAneiangYarpClient(options =>
{
    options.InstanceId = "dev-john";
    options.InstancePrefixFormat = "dev-{instanceId}";
});
```

Disable if not needed:
```csharp
options.InstanceIsolation = false;
```

</details>

<details>
<summary><b>🌐 Automatic LAN IP Resolution</b></summary>

When `DestinationAddress` contains `localhost` / `127.0.0.1` / `0.0.0.0`:

```
Configured:  http://localhost:5001
Resolved:    http://192.168.1.101:5001  (LAN IP)
```

**Special Handling:**
- Automatically detects local loopback addresses
- Resolves to first available LAN IP for cross-machine accessibility
- Critical for auto-registration: other machines can reach the service

Disable: `AutoResolveIp = false`

</details>

<details>
<summary><b>🚪 Conditional API Exposure (enableRegistration)</b></summary>

Control whether the gateway exposes dynamic route registration APIs:

```csharp
// Enable registration API (default)
builder.Services.AddAneiangYarp(enableRegistration: true);

// Disable registration API (security hardening)
builder.Services.AddAneiangYarp(enableRegistration: false);
```

**Special Handling:**
- When `enableRegistration = false`, `GatewayConfigController` is completely removed from MVC application model
- Returns **404 Not Found** (not 401/403) — no endpoint exists at all
- Recommended for production gateways that should not accept external route changes
- Does NOT affect YARP proxy functionality or Dashboard access

</details>

<details>
<summary><b>🔧 Custom Middleware Pipeline</b></summary>

```csharp
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRequestLogging();
app.UseRateLimiter();

app.UseRouting();
app.MapControllers();
app.MapReverseProxy();  // Must be last
```

</details>

---

## 📖 Configuration

<details>
<summary><b>Gateway:Registration</b> — Full Configuration Options</summary>

```json
{
  "Gateway": {
    "Registration": {
      "GatewayUrl": "http://192.168.1.100:5000",
      "RouteName": "my-service",
      "ClusterName": "my-service-cluster",
      "MatchPath": "/api/my-service/{**catch-all}",
      "DestinationAddress": "http://localhost:5001",
      "Order": 50,
      "AutoResolveIp": true,
      "TimeoutSeconds": 10,
      "InstanceIsolation": true,
      "InstanceId": "john",
      "InstancePrefixFormat": "{instanceId}",
      "StripInstancePrefix": true,
      "DownstreamPathPrefix": null,
      "Transforms": [],
      "AuthToken": null,
      "ApiKey": null,
      "BasicAuthUsername": null,
      "BasicAuthPassword": null
    }
  }
}
```

**Configuration Priority:** Code `options => {}` > Environment Variables > `appsettings.json`

</details>

<details>
<summary><b>Gateway:Dashboard</b> — Dashboard Configuration</summary>

```json
{
  "Gateway": {
    "Dashboard": {
      "EnableProxyLogging": true,
      "AuthMode": "DefaultJwt",
      "JwtPassword": "demo123",
      "RoutePrefix": "apigateway",
      "Locale": "en-US"
    }
  }
}
```

**Auth Modes:** `None` | `ApiKey` | `CustomJwt` | `DefaultJwt`

**Special Handling:**
- JWT tokens expire in **8 hours** by default
- Login via `POST /apigateway/login` to get token
- When `AuthMode` is configured, `AddGatewayApiAuth()` can auto-detect credentials from `JwtPassword`
- `RoutePrefix` allows customizing the dashboard URL path (default: `apigateway`)
- `Locale` supports runtime language switching: `en-US` | `zh-CN`

</details>

<details>
<summary><b>ReverseProxy</b> — Standard YARP Configuration</b></summary>

```json
{
  "ReverseProxy": {
    "Routes": {
      "my-route": {
        "ClusterId": "my-cluster",
        "Match": "/api/test/{**catch-all}"
      }
    },
    "Clusters": {
      "my-cluster": {
        "Destinations": {
          "d1": { "Address": "http://localhost:5000/" }
        },
        "LoadBalancingPolicy": "PowerOfTwoChoices"
      }
    }
  }
}
```

See [YARP docs](https://microsoft.github.io/reverse-proxy/articles/config-files.html) for details.

</details>

---

## 🔌 API Endpoints

<details>
<summary><b>Gateway API</b> — <code>/api/gateway</code></summary>

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/gateway/register-route` | `POST` | Register/update route |
| `/api/gateway/{routeName}` | `DELETE` | Unregister route |
| `/api/gateway/routes` | `GET` | Query all routes |
| `/api/gateway/ping` | `GET` | Health check |

**Request Example:**
```json
{
  "routeName": "my-service",
  "clusterName": "my-service-cluster",
  "matchPath": "/api/my-service/{**catch-all}",
  "destinationAddress": "http://192.168.1.101:5001",
  "order": 50,
  "transforms": [
    { "PathSet": "/api/backend/{**catch-all}" }
  ]
}
```

**Response Format:**
```json
{ "code": 200, "message": "Route registered successfully" }
```

</details>

<details>
<summary><b>Dashboard API</b> — <code>/apigateway</code></summary>

| Endpoint | Description |
|----------|-------------|
| `GET /apigateway` | Dashboard home page |
| `GET /apigateway/login` | Login page |
| `POST /apigateway/login` | Login (returns JWT token) |
| `GET /apigateway/info` | Gateway runtime info |
| `GET /apigateway/clusters` | Cluster status |
| `GET /apigateway/routes` | Route configuration |
| `GET /apigateway/routes/{routeId}` | Route details |
| `GET /apigateway/logs` | Recent YARP logs |
| `DELETE /apigateway/logs` | Clear logs |

</details>

---

## 📂 Project Structure

```
Aneiang.Yarp/
├── src/
│   ├── Aneiang.Yarp/                  # Core library
│   │   ├── Controllers/               # Gateway REST API
│   │   ├── Extensions/                # DI registration
│   │   ├── Models/                    # DTOs & Options
│   │   └── Services/                  # Core services
│   │
│   └── Aneiang.Yarp.Dashboard/        # Dashboard library
│       ├── Controllers/               # Dashboard API
│       ├── Views/                     # Razor views
│       ├── Services/                  # Dashboard services
│       ├── Models/                    # Dashboard models
│       └── Extensions/                # DI registration
│
└── samples/
    ├── SampleGateway/                 # Gateway example
    └── SampleLocalService/            # Client example
```

---

## 🧪 Sample Projects

```bash
# Terminal 1: Start gateway
dotnet run --project samples/SampleGateway

# Terminal 2: Start local service (auto-registers)
dotnet run --project samples/SampleLocalService

# Test the route
curl http://localhost:5000/api/your-endpoint
```

**Sample Gateway Features:**
- Dashboard at `/apigateway` (login: `admin` / `demo123`)
- Serilog logging
- JWT authentication

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).

---

<div align="center">

**Made with ❤️ for the .NET community**

[⭐ Star this repo](https://github.com/aneiang/Aneiang.Yarp) if you find it useful!

</div>
