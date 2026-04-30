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

## ✨ Features

| Feature | Description |
|---------|-------------|
| 🚀 **Dynamic Routing** | REST API for runtime route registration/update/unregistration |
| 🔄 **Auto-Registration** | Services auto-register on startup & unregister on shutdown — **1 line of code** *(requires network connectivity, ideal for debugging)* |
| 🎯 **One-Line API** | `AddAneiangYarp()` / `AddAneiangYarpClient()` to setup gateway or client |
| 👥 **Instance Isolation** | Automatic namespace isolation for multi-developer debugging |
| ⚙️ **Highly Customizable** | Code > Env Vars > Config files priority; fine-grained component control |
| 📊 **Dashboard (Recommended)** | Real-time clusters, routes, health status & YARP logs viewer |
| 🧠 **Smart Defaults** | Auto-detect assembly name, Kestrel address, resolve localhost to LAN IP |

## 📦 NuGet Packages

| Package | Description | Link |
|---------|-------------|------|
| **Aneiang.Yarp** | Core library: dynamic routing + auto-registration client | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp) |
| **Aneiang.Yarp.Dashboard** | 🌟 **Recommended**: Dashboard for monitoring & operations | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard) |

**Requirements:**
- Target Framework: `.NET 8.0` / `.NET 9.0`
- YARP Version: `2.3.0`

---

## 🚀 Quick Start

### 1️⃣ Setup Gateway

```csharp
// Program.cs
using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ⭐ One-line gateway setup
builder.Services.AddAneiangYarp();

// Optional: Add dashboard
builder.Services.AddAneiangYarpDashboard();

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.MapReverseProxy();
app.Run();
```

### 2️⃣ Connect Client Service

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
<summary><b>🛡️ Gateway API Authorization</b></summary>

Protect registration APIs with BasicAuth or ApiKey:

```csharp
// Auto-detect from Dashboard config
builder.Services.AddGatewayApiAuth();

// Or explicit configuration
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

</details>

<details>
<summary><b>👥 Instance Isolation (Multi-Developer)</b></summary>

**Enabled by default** — automatically embeds machine identifier into routes:

| Dimension | DevA (PC-JOHN) | DevB (PC-JANE) |
|-----------|----------------|----------------|
| routeName | `my-service-PC-JOHN` | `my-service-PC-JANE` |
| matchPath | `/PC-JOHN/api/{**catch-all}` | `/PC-JANE/api/{**catch-all}` |

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

Disable: `AutoResolveIp = false`

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

JWT tokens expire in **8 hours**. Login via `POST /apigateway/login`.

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
