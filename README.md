<div align="center">
<img src="Logo.png" alt="LOGO" width="240" style="border-radius: 15px;"/>

**A full-featured YARP gateway enhancement — dashboard, dynamic routing, auto-registration, and IP isolation**

[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp)
[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard)
[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Client.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Client)
[![YARP](https://img.shields.io/badge/YARP-2.3.0-blue.svg)](https://github.com/microsoft/reverse-proxy)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-purple.svg)](https://dotnet.microsoft.com/)

[English](README.md) | [中文](README.zh-CN.md)

</div>

---

Aneiang.Yarp is an enhanced API gateway solution built on top of [Microsoft YARP](https://microsoft.github.io/reverse-proxy/) 2.3.0. It provides three NuGet packages that work together to solve real-world gateway challenges:

| Package | Purpose | YARP Dependency |
|---------|---------|:---:|
| **Aneiang.Yarp** | Gateway core: dynamic routing, config persistence, API auth, IP-based load balancing | Yes |
| **Aneiang.Yarp.Client** | Client auto-registration: one-liner to register/unregister with the gateway | No |
| **Aneiang.Yarp.Dashboard** | Web management UI: cluster/route CRUD, config import/export, snapshot rollback, real-time logs | Via core |

```
Dependencies:

Aneiang.Yarp.Dashboard
  └── Aneiang.Yarp
        └── Aneiang.Yarp.Client

Client services → Reference Aneiang.Yarp.Client only (no YARP SDK pulled in)
Gateway services → Reference Aneiang.Yarp + Aneiang.Yarp.Dashboard
```

Demo: http://113.45.65.71:8930/apigateway &nbsp;&nbsp; admin/demo123

---

## Dashboard Preview

<table>
  <tr>
    <td align="center"><b>Cluster Management</b></td>
    <td align="center"><b>Route Management</b></td>
  </tr>
  <tr>
    <td><img src="docs/cluster-list.png" alt="Cluster List" width="480"/></td>
    <td><img src="docs/route-list.png" alt="Route List" width="480"/></td>
  </tr>
  <tr>
    <td align="center"><b>JSON Editor</b></td>
    <td align="center"><b>Request Logs</b></td>
  </tr>
  <tr>
    <td><img src="docs/cluster-create.png" alt="Cluster Editor" width="480"/></td>
    <td><img src="docs/log-list.png" alt="Request Logs" width="480"/></td>
  </tr>
</table>

<div align="center">
<img src="docs/overview.png" alt="Overview" width="720"/>
</div>

---

## Features

### Dynamic Route Management

YARP relies on `appsettings.json` for route configuration, requiring restarts for changes. Aneiang.Yarp adds full runtime management:

- **Dual config sources**: Static config from `appsettings.json` + dynamic config from API calls, unified in YARP's `InMemoryConfigProvider`
- **Config persistence**: All dynamic changes auto-persist to `gateway-dynamic.json`, surviving restarts
- **Thread-safe**: All operations protected by `lock` for concurrent safety
- **Source tracking**: Every route/cluster records `CreatedAt`, `CreatedBy`, `Source` metadata

RESTful API at `api/gateway`:

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/gateway/register-route` | Register or update a route + cluster |
| DELETE | `/api/gateway/{routeName}?clientIp=` | Delete a route (supports IP isolation) |
| GET | `/api/gateway/routes` | Query all routes |
| GET | `/api/gateway/dynamic-config` | Query dynamic config with metadata |
| PUT | `/api/gateway/routes/{routeId}` | Update a route |
| POST | `/api/gateway/clusters` | Create a cluster |
| PUT | `/api/gateway/clusters/{clusterId}` | Update a cluster |
| DELETE | `/api/gateway/clusters/{clusterId}` | Delete a cluster |
| GET | `/api/gateway/ping` | Health check |

### Client Auto-Registration

Microservices can auto-register with the gateway on startup and auto-unregister on shutdown — one line of code:

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

**Smart defaults** — only `GatewayUrl` is required. Everything else is auto-detected:

| Config | Default | Description |
|--------|---------|-------------|
| `RouteName` | Entry assembly name | e.g. `MyService` |
| `ClusterName` | Same as RouteName | |
| `MatchPath` | `/{**catch-all}` | Match all paths |
| `DestinationAddress` | Kestrel bind address | Auto-detected, `localhost` resolved to LAN IP |
| `Order` | `50` | Route priority |

> `Aneiang.Yarp.Client` has **no YARP SDK dependency** — it only depends on `Microsoft.AspNetCore.App`. Downstream services get a clean dependency footprint.

### IP Isolation Load Balancing

A unique feature for **multi-developer debugging**. Multiple developers run the same service locally, and the gateway routes requests by client IP — the frontend is completely unaware.

```
Developer A (192.168.1.10) → POST /api/user → Gateway → 192.168.1.10:5001
Developer B (192.168.1.20) → POST /api/user → Gateway → 192.168.1.20:5001
Other requests                  → POST /api/user → Gateway → First available instance
```

Enable on the client side:

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

Implementation: Custom `IpBasedLoadBalancingPolicy` matches request source IP (via `X-Forwarded-For` or `RemoteIpAddress`) against destination metadata.

### Dashboard

Full web management UI — 2 lines of code to enable:

```csharp
builder.Services.AddAneiangYarpDashboard();
// ...
app.UseAneiangYarpDashboard();
```

| Feature | Description |
|---------|-------------|
| Cluster & Route CRUD | Form-based + JSON editor, supports YARP standard format |
| Config Import/Export | One-click export/import in standard YARP format with validation |
| Snapshot & Rollback | Auto-snapshot before every change, one-click rollback |
| Real-time Logs | Request/response capture with route/status/TraceID filtering |
| Log Sanitization | Auto-mask sensitive headers, query params, and JSON fields |
| Log Sampling | Rate-based sampling for production traffic control |
| i18n | Chinese / English, runtime switchable |
| Multi-mode Auth | None / API Key / JWT (default & custom) / custom delegate |

### API Authorization

Protect the gateway management API (`GatewayConfigController`) with optional auth:

```csharp
builder.Services.AddGatewayApiAuth();
```

| Mode | Description |
|------|-------------|
| `BasicAuth` | HTTP Basic authentication |
| `ApiKey` | Via `X-Api-Key` header |
| `None` | No protection (default) |

```json
{
  "Gateway": {
    "ApiAuth": {
      "Mode": "BasicAuth",
      "Username": "admin",
      "Password": "secure-password"
    }
  }
}
```

Smart inference: If `Gateway:ApiAuth` is not configured but `Gateway:Dashboard` has a JWT password, credentials are auto-derived (username `admin`, password = JWT password).

---

## Quick Start

### 1. Create a Gateway

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
app.UseAneiangYarpDashboard();
app.MapControllers();
app.MapReverseProxy();
app.Run();
```

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
builder.Services.AddAneiangYarpClient();
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

That's it. The microservice registers itself on startup and unregisters on shutdown.

---

## Dashboard Configuration

All settings go under `Gateway:Dashboard`. Works out of the box without any config — every option has a default.

```json
{
  "Gateway": {
    "Dashboard": {
      "EnableProxyLogging": true,
      "RoutePrefix": "apigateway",
      "Locale": "en-US",

      "AuthMode": "DefaultJwt",
      "JwtPassword": "your-strong-password",
      "JwtUsername": "admin",
      "JwtSecret": "...",
      "ApiKey": "your-api-key",
      "ApiKeyHeaderName": "X-Api-Key",

      "EnableLogSampling": false,
      "LogSamplingRate": 1.0,
      "LogErrorsOnly": false,
      "LogMaxBodyLength": 8192,

      "LogRouteWhitelist": [],
      "LogRouteBlacklist": [],
      "LogHeaderBlacklist": ["Authorization", "Cookie", "Set-Cookie"],
      "LogQueryBlacklist": [],
      "LogJsonFieldSanitizeList": ["password", "token", "secret", "apikey", "api-key"]
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `EnableProxyLogging` | `true` | Master switch for request logging |
| `RoutePrefix` | `"apigateway"` | Dashboard URL prefix |
| `Locale` | `"en-US"` | Default language, switchable at runtime (`en-US` / `zh-CN`) |
| `AuthMode` | `None` | `None` / `ApiKey` / `CustomJwt` / `DefaultJwt` |
| `JwtPassword` | null | JWT login password |
| `JwtUsername` | null | CustomJwt username (DefaultJwt uses fixed `admin`) |
| `JwtSecret` | null | JWT signing key — auto-generated if not set (resets on restart) |
| `ApiKey` | null | API Key value |
| `ApiKeyHeaderName` | `"X-Api-Key"` | Header name for API Key |
| `EnableLogSampling` | `false` | Enable log sampling |
| `LogSamplingRate` | `1.0` | Sampling rate 0.0–1.0 |
| `LogErrorsOnly` | `false` | Only log errors (4xx/5xx) |
| `LogMaxBodyLength` | `8192` | Max body length in bytes |
| `LogRouteWhitelist` | null | Route whitelist |
| `LogRouteBlacklist` | null | Route blacklist |
| `LogHeaderBlacklist` | null | Header sanitization list |
| `LogQueryBlacklist` | null | Query param sanitization list |
| `LogJsonFieldSanitizeList` | null | JSON field sanitization list |

### Dashboard Endpoints

#### Pages — `/{RoutePrefix}`

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/{prefix}` | GET | Dashboard home |
| `/{prefix}/login` | GET/POST | Login page / authenticate |
| `/{prefix}/logout` | POST | Logout |
| `/{prefix}/info` | GET | Gateway runtime info |
| `/{prefix}/clusters` | GET | Cluster list |
| `/{prefix}/routes` | GET | Route list |
| `/{prefix}/logs` | GET/DELETE | Query logs / clear all |
| `/{prefix}/auth/status` | GET | Auth status |

#### Config Management — `/{RoutePrefix}/api/config`

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/config/export` | GET | Export full YARP config |
| `/api/config/import` | POST | Import config (validate + snapshot + persist) |
| `/api/config/validate` | POST | Validate config format |
| `/api/config/history` | GET | Change history |
| `/api/config/rollback/{id}` | POST | Rollback to a specific version |
| `/api/config/routes/{id}` | PUT/DELETE | Create/update / delete a route |
| `/api/config/clusters/{id}` | PUT/DELETE | Create/update / delete a cluster |
| `/api/config/clusters/{id}/rename` | PUT | Rename a cluster |

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

When the gateway has Dashboard auth configured, `AddGatewayApiAuth()` auto-reads the JWT password as the API auth credential:

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
app.UseAneiangYarpDashboard();  // ← after UseRouting
app.MapControllers();
app.MapReverseProxy();           // ← must be last
```

The middleware captures YARP proxy request/response data and automatically skips the dashboard's own requests.

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
      "LogJsonFieldSanitizeList": ["password", "token", "secret", "apikey", "creditCard", "ssn"]
    }
  }
}
```

</details>

---

## Sample Projects

```bash
# Start the gateway (with dashboard)
dotnet run --project samples/SampleGateway

# Start the client (auto-registers to gateway)
dotnet run --project samples/SampleLocalService

# Test
curl http://localhost:5000/api/your-endpoint
```

Dashboard: `/apigateway`, login: `admin` / `demo123`

---

## NuGet

| Package | Description | Link |
|---------|-------------|------|
| **Aneiang.Yarp** | Gateway core: dynamic routing, IP isolation, API auth | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp) |
| **Aneiang.Yarp.Client** | Client auto-registration (lightweight, no YARP dependency) | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Client.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Client) |
| **Aneiang.Yarp.Dashboard** | Web management UI with auth, logs, and config management | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard) |

**Supports** .NET 8.0 / .NET 9.0 · YARP 2.3.0

---

## License

[MIT](LICENSE) — use it however you like.

---

<div align="center">

Find it useful? [⭐ Star this repo](https://github.com/aneiang/Aneiang.Yarp) to help others find it

</div>
