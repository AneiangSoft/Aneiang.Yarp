# Aneiang.Yarp.Dashboard

<div align="center">

**Give your YARP gateway a management dashboard — in just 3 lines of code**

[![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard)
[![YARP](https://img.shields.io/badge/YARP-2.3.0-blue.svg)](https://github.com/microsoft/reverse-proxy)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-purple.svg)](https://dotnet.microsoft.com/)

[English](README.md) | [中文](README.zh-CN.md)

</div>

---

Using YARP as your gateway, but still editing `appsettings.json` by hand to manage routes?

**Aneiang.Yarp.Dashboard** lets you manage YARP right in the browser — visual CRUD, JSON editor, one-click rollback, real-time logs. Install and go.

```csharp
// Program.cs — that's it
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpDashboard();  // ← add this line
```

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

## Get Started in 30 Seconds

### 1. Install NuGet Packages

```bash
dotnet add package Aneiang.Yarp
dotnet add package Aneiang.Yarp.Dashboard
```

### 2. Update Program.cs

```csharp
using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpDashboard();  // ← add this line, done

var app = builder.Build();
app.UseRouting();
app.UseAneiangYarpDashboard();  // ← add this line, captures request logs
app.MapControllers();
app.MapReverseProxy();
app.Run();
```

### 3. Open the Browser

Visit `http://localhost:5000/apigateway` — the dashboard is already there.

### Want a Login? (Optional)

```json
// appsettings.json
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "your-password"
    }
  }
}
```

Add the config, and the dashboard redirects to a login page. Default username: `admin`.

---

## What You Get

| Feature | What It Does |
|---------|-------------|
| 📊 **Cluster Management** | Browse, create, edit, delete, rename clusters — or use the JSON editor |
| 🛣️ **Route Management** | Full CRUD + JSON editor, supports standard YARP format |
| 💾 **Config Import/Export** | One-click export of full YARP config, import with auto-validation |
| ⏪ **Config Rollback** | Auto-snapshot on every change, one-click rollback if something breaks |
| 📝 **Real-time Logs** | Full request/response capture, filter by route/status/TraceID |
| 🔐 **Multi-mode Auth** | JWT login / API Key / custom delegate — works out of the box |
| 🌐 **i18n** | Switch language at runtime, no restart needed |
| 🛡️ **Log Sanitization** | Auto-mask Authorization, password, and other sensitive fields |
| 📈 **Log Sampling** | Sample by rate in production to control log volume |

**Design principle**: Zero intrusion — just 2 lines of code to enable, doesn't change your existing YARP config or proxy logic.

---

## Feature Details

### 📊 Cluster & Route Management

Manage YARP clusters and routes right in the browser — no more hand-editing JSON config files:

- **Form-based creation** — Fill in a few fields to create a route or cluster
- **JSON editor** — Built-in code editor with syntax highlighting, live validation, auto-formatting
- **Format compatibility** — Accepts both camelCase and PascalCase, paste official YARP config directly
- **Smart association** — Creating a route whose target cluster doesn't exist? It auto-creates the cluster
- **Safe deletion** — Optionally clean up orphaned clusters when deleting routes; deleting a cluster auto-updates references

### 💾 Config Version Management

Broke something? One-click rollback.

- **Auto-snapshot** — Before every create/edit/delete, the system automatically saves a config snapshot
- **Change history** — View timestamp, operator IP, and change content for each change
- **One-click rollback** — Pick any historical version and roll back instantly
- **Export** — Export in full YARP standard format, ready to paste into `appsettings.json`
- **Import** — Import from JSON, auto-validate format, merge and persist

### 📝 Real-time Request Logs

See what happens to every request passing through the gateway:

- Request method, path, query parameters, headers
- Response status code, headers
- Request/response body (JSON auto-parsed)
- Target cluster and route
- Trace ID, request duration

**Filtering**: by route ID / status code / Trace ID / time range

**Security controls** (production-friendly):

| Setting | One-liner |
|---------|-----------|
| `EnableProxyLogging` | Master switch — off stops the entire pipeline |
| `EnableLogSampling` + `LogSamplingRate` | Sampling — 0.1 = only log 10% |
| `LogErrorsOnly` | Only log 4xx/5xx |
| `LogMaxBodyLength` | Truncate long bodies |
| `LogRouteWhitelist` / `LogRouteBlacklist` | Filter by route |
| `LogHeaderBlacklist` | `Authorization` → `***REDACTED***` |
| `LogQueryBlacklist` | Mask sensitive query params |
| `LogJsonFieldSanitizeList` | `password` in JSON → `***REDACTED***` |

### 🔐 Authentication

Four modes, pick what fits:

| Mode | How It Works | Best For |
|------|-------------|----------|
| `None` | No protection | Local development |
| `DefaultJwt` | Set a password, username is fixed as `admin` | Personal / small teams |
| `CustomJwt` | Custom username + password | Enterprise |
| `ApiKey` | Pass API Key via Header | API integrations |

There's also an `AuthorizeRequest` delegate you can use to plug in your own auth system (highest priority).

---

## Configuration Reference

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
| `Locale` | `"en-US"` | Default language, switchable at runtime |
| `AuthMode` | `None` | `None` / `ApiKey` / `CustomJwt` / `DefaultJwt` |
| `JwtPassword` | null | JWT login password |
| `JwtUsername` | null | CustomJwt username (DefaultJwt uses fixed `admin`) |
| `JwtSecret` | null | JWT signing key — auto-generated if not set (resets on restart) |
| `ApiKey` | null | API Key value |
| `ApiKeyHeaderName` | `"X-Api-Key"` | Header name for API Key |
| `EnableLogSampling` | `false` | Enable log sampling |
| `LogSamplingRate` | `1.0` | Sampling rate 0.0–1.0 |
| `LogErrorsOnly` | `false` | Only log errors |
| `LogMaxBodyLength` | `8192` | Max body length in bytes |
| `LogRouteWhitelist` | null | Route whitelist |
| `LogRouteBlacklist` | null | Route blacklist |
| `LogHeaderBlacklist` | null | Header sanitization list |
| `LogQueryBlacklist` | null | Query param sanitization list |
| `LogJsonFieldSanitizeList` | null | JSON field sanitization list |

---

## API Endpoints

### Dashboard — `/{RoutePrefix}`

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

### Config Management — `/{RoutePrefix}/api/config`

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

> Default prefix is `apigateway`, customizable via `RoutePrefix`.

---

## Advanced Usage

<details>
<summary><b>🔐 Custom Authorization — Plug Into Your Own Auth System</b></summary>

```csharp
builder.Services.AddAneiangYarpDashboard(options =>
{
    options.AuthorizeRequest = async (context) =>
    {
        // Return true = allow access
        return context.User.Identity?.IsAuthenticated == true
            && context.User.IsInRole("GatewayAdmin");
    };
});
```

Priority: `AuthorizeRequest` > `ApiKey` > `JWT` > `None`

</details>

<details>
<summary><b>🛡️ Dashboard + Zero-Config Client Registration</b></summary>

When the gateway has Dashboard auth configured, `AddGatewayApiAuth()` auto-reads the password — zero config on the client side:

```csharp
// Gateway Program.cs
builder.Services.AddAneiangYarp();
builder.Services.AddAneiangYarpDashboard();
builder.Services.AddGatewayApiAuth();  // Auto-reads Dashboard password

// Client Program.cs — no auth config needed at all
builder.Services.AddAneiangYarpClient();
```

</details>

<details>
<summary><b>🔧 Middleware Order</b></summary>

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

The middleware captures YARP proxy request/response data and automatically skips dashboard's own requests.

</details>

<details>
<summary><b>📋 Production Recommended Config</b></summary>

```json
{
  "Gateway": {
    "Dashboard": {
      "AuthMode": "DefaultJwt",
      "JwtPassword": "very-strong-password-here",
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

## About Aneiang.Yarp

The dashboard depends on [Aneiang.Yarp](https://www.nuget.org/packages/Aneiang.Yarp) core library, which provides:

| Capability | Description |
|------------|-------------|
| 🚀 **Dynamic Routing API** | `/api/gateway` for runtime route registration/update/unregistration |
| 🔄 **Auto-Registration Client** | `AddAneiangYarpClient()` — one line, registers on startup, unregisters on shutdown |
| 👥 **Instance Isolation** | Multi-developer debugging with automatic namespace isolation |
| 🧠 **Smart Defaults** | Auto-detect assembly name, Kestrel address, resolve localhost → LAN IP |
| 🛡️ **API Authorization** | Optional BasicAuth/ApiKey protection for registration APIs |
| 🚪 **Conditional API Exposure** | `enableRegistration: false` removes registration endpoints entirely |

The core library works independently — no dashboard required.

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
| **Aneiang.Yarp.Dashboard** | Dashboard | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.Dashboard.svg)](https://www.nuget.org/packages/Aneiang.Yarp.Dashboard) |
| **Aneiang.Yarp** | Core library (standalone) | [![NuGet](https://img.shields.io/nuget/v/Aneiang.Yarp.svg)](https://www.nuget.org/packages/Aneiang.Yarp) |

**Supports** .NET 8.0 / .NET 9.0 · YARP 2.3.0

---

## License

[MIT](LICENSE) — use it however you like.

---

<div align="center">

Find it useful? [⭐ Star this repo](https://github.com/aneiang/Aneiang.Yarp) to help others find it

</div>
