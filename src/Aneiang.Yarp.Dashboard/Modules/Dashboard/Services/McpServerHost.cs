using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;

/// <summary>
/// Model Context Protocol (MCP) server exposing gateway READ tools over the
/// Streamable HTTP transport (RFC-compliant + Trae-compatible).
///
/// Supports both the full MCP protocol (initialize, tools/list, tools/call)
/// and the legacy 2026-07-28 custom protocol (server/discover, _meta envelope).
///
/// Only read-only tools are exposed — write operations are never allowed via MCP.
/// </summary>
public sealed class McpServerHost : BackgroundService
{
    public const string ProtocolVersion = "2025-06-18";

    private const string MetaProtocolVersion = "io.modelcontextprotocol/protocolVersion";
    private const string MetaServerInfo = "io.modelcontextprotocol/serverInfo";

    private static readonly JsonArray Tools = new()
    {
        Tool("list_routes", "List all YARP routes with RouteId, cluster, match path, and enabled status."),
        Tool("list_clusters", "List all YARP clusters with ClusterId, destinations, and load balancing policy."),
        Tool("get_cluster_health", "Get health status of all destinations across all clusters."),
        Tool("get_traffic_stats", "Get traffic statistics for a recent time window.", new JsonObject
        {
            ["minutes"] = new JsonObject { ["type"] = "integer", ["description"] = "Time window in minutes (default: 60)" }
        }),
        Tool("list_plugins", "List all installed plugins with name, enabled status, and category."),
        Tool("get_config_history", "Get recent configuration change history.", new JsonObject
        {
            ["count"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of recent changes (default: 10)" }
        })
    };

    private static readonly JsonObject ServerInfo = new()
    {
        ["name"] = "Aneiang.Yarp.Mcp",
        ["version"] = "1.0.0"
    };

    private static readonly JsonObject Capabilities = new()
    {
        ["tools"] = new JsonObject { ["listChanged"] = false }
    };

    private static readonly string[] SupportedVersions = { ProtocolVersion, "2026-07-28", "2025-06-18", "2024-11-05" };

    private readonly AIConfigStore _config;
    private readonly IServiceProvider _services;
    private readonly ILogger<McpServerHost> _logger;

    public McpServerHost(
        AIConfigStore config,
        IServiceProvider services,
        ILogger<McpServerHost> logger)
    {
        _config = config;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        WebApplication? app = null;
        var activePort = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var mcp = _config.Current.McpServer;
            try
            {
                if (mcp.Enabled && app == null)
                {
                    int requestedPort = mcp.Port;
                    if (requestedPort <= 0 || requestedPort > 65535)
                    {
                        _logger.LogWarning("MCP server port {Port} is invalid; using default 8090", requestedPort);
                        requestedPort = 8090;
                    }

                    _logger.LogInformation("MCP server starting: configured port={Port}, config file={ConfigFile}", requestedPort, _config.ConfigPath);

                    int boundPort = FindAvailablePort(requestedPort);
                    if (boundPort != requestedPort)
                    {
                        _logger.LogWarning("MCP server port {Requested} is already in use; falling back to {Actual}. Update Trae MCP config to use port {Actual}.", requestedPort, boundPort, boundPort);
                    }

                    app = BuildApp(boundPort);
                    await app.StartAsync(stoppingToken);
                    activePort = boundPort;
                    _logger.LogInformation("MCP server listening on http://0.0.0.0:{Port}/mcp (protocol {Version})", activePort, ProtocolVersion);
                }
                else if ((!mcp.Enabled || mcp.Port != activePort) && app != null)
                {
                    await app.StopAsync(CancellationToken.None);
                    await app.DisposeAsync();
                    app = null;
                    activePort = 0;
                    _logger.LogInformation("MCP server stopped");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MCP server lifecycle error; will retry");
                if (app != null) { await app.DisposeAsync(); app = null; }
                activePort = 0;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        if (app != null)
        {
            try { await app.StopAsync(CancellationToken.None); } catch { }
            await app.DisposeAsync();
        }
    }

    private static int FindAvailablePort(int requestedPort)
    {
        for (int offset = 0; offset < 10; offset++)
        {
            int candidate = requestedPort + offset;
            if (candidate > 65535) break;

            try
            {
                using var probe = new TcpListener(System.Net.IPAddress.Loopback, candidate);
                probe.Start();
                probe.Stop();
                return candidate;
            }
            catch
            {
                // Port is in use, try next
            }
        }

        for (int candidate = 8090; candidate < 65535; candidate++)
        {
            try
            {
                using var probe = new TcpListener(System.Net.IPAddress.Loopback, candidate);
                probe.Start();
                probe.Stop();
                return candidate;
            }
            catch
            {
                // Try next
            }
        }

        throw new InvalidOperationException("No available TCP port found for MCP server.");
    }

    private WebApplication BuildApp(int port)
    {
        var aiTools = _services.GetRequiredService<AIFunctionService>();

        // Isolated content root: prevents the slim builder from loading the gateway host's
        // appsettings.json, whose "Kestrel:Endpoints" section would override our port
        // (e.g. MCP silently trying to bind the Dashboard port 5200).
        var contentRoot = Path.Combine(Path.GetTempPath(), "aneiang-mcp");
        Directory.CreateDirectory(contentRoot);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRoot,
            EnvironmentName = "Mcp",
            ApplicationName = "Aneiang.Yarp.Mcp"
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddProvider(new ForwardingLoggerProvider(_logger));

        // Explicit listen endpoint: highest precedence in Kestrel, immune to
        // IConfiguration endpoints and the ASPNETCORE_URLS environment variable.
        builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            context.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type, Accept, MCP-Protocol-Version, Mcp-Method, Mcp-Name, Mcp-Session-Id");
            context.Response.Headers.Append("Access-Control-Expose-Headers", "MCP-Session-Id");

            if (context.Request.Method == "OPTIONS")
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await next();
        });

        app.MapGet("/mcp", (HttpContext ctx) => HandleGetAsync(ctx));
        app.MapPost("/mcp", (HttpContext ctx) => HandlePostAsync(ctx, aiTools));
        app.MapMethods("/mcp", new[] { "DELETE", "PUT", "PATCH" }, (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            ctx.Response.Headers.Append("Allow", "GET, POST");
            ctx.Response.ContentType = "application/json";
            return ctx.Response.WriteAsync("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32600,\"message\":\"Method not allowed. Use GET for health check or POST for JSON-RPC.\"}}");
        });

        return app;
    }

    private static async Task HandleGetAsync(HttpContext ctx)
    {
        ctx.Response.ContentType = "application/json";
        var response = new JsonObject
        {
            ["status"] = "ok",
            ["service"] = "Aneiang.Yarp.Mcp",
            ["version"] = "1.0.0",
            ["protocolVersion"] = ProtocolVersion,
            ["endpoints"] = new JsonObject
            {
                ["initialize"] = "/mcp (POST initialize)",
                ["discover"] = "/mcp (POST server/discover)",
                ["tools"] = "/mcp (POST tools/list)",
                ["call"] = "/mcp (POST tools/call)"
            },
            ["tools"] = CloneTools()
        };
        await ctx.Response.WriteAsync(response.ToJsonString(), ctx.RequestAborted);
    }

    private static async Task HandlePostAsync(HttpContext ctx, AIFunctionService tools)
    {
        var accept = ctx.Request.Headers.Accept.ToString();
        var wantsSse = accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);

        JsonDocument doc;
        try
        {
            doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
        }
        catch
        {
            await WriteJsonRpcErrorAsync(ctx, null, -32700, "Parse error: request body is not valid JSON.", wantsSse);
            return;
        }

        using (doc)
        {
            var request = doc.RootElement;
            var method = request.TryGetProperty("method", out var m) ? m.GetString() : null;
            var hasId = request.TryGetProperty("id", out var idEl);
            var id = hasId && idEl.ValueKind != JsonValueKind.Null ? JsonNode.Parse(idEl.GetRawText()) : null;

            if (string.IsNullOrWhiteSpace(method))
            {
                await WriteJsonRpcErrorAsync(ctx, id, -32600, "Invalid Request: missing method.", wantsSse);
                return;
            }

            var response = await BuildResponseAsync(request, method, id, tools);

            if (response == null)
            {
                ctx.Response.StatusCode = StatusCodes.Status202Accepted;
                if (wantsSse)
                {
                    ctx.Response.ContentType = "text/event-stream";
                    await ctx.Response.WriteAsync("data: {}\n\n", ctx.RequestAborted);
                }
                return;
            }

            if (wantsSse)
            {
                await WriteSseResponseAsync(ctx, response);
            }
            else
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(response.ToJsonString(), ctx.RequestAborted);
            }
        }
    }

    private static async Task WriteSseResponseAsync(HttpContext ctx, JsonObject response)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.Append("Cache-Control", "no-cache");
        ctx.Response.Headers.Append("X-Accel-Buffering", "no");
        await ctx.Response.WriteAsync($"data: {response.ToJsonString()}\n\n", ctx.RequestAborted);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
    }

    /// <summary>
    /// JsonNode instances can only be attached to one parent; deep-clone the shared
    /// static definitions per response so repeated requests don't throw
    /// "node already has a parent" and return HTTP 500.
    /// </summary>
    private static JsonArray CloneTools() => (JsonArray)Tools.DeepClone();

    private static async Task<JsonObject?> BuildResponseAsync(JsonElement request, string? method, JsonNode? id, AIFunctionService tools)
    {
        var prm = request.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object ? p.Clone() : new JsonElement();

        switch (method)
        {
            case "initialize":
            {
                var clientVersion = prm.ValueKind == JsonValueKind.Object && prm.TryGetProperty("protocolVersion", out var pv)
                    ? pv.GetString()
                    : null;

                var negotiateVersion = !string.IsNullOrEmpty(clientVersion) && SupportedVersions.Contains(clientVersion, StringComparer.Ordinal)
                    ? clientVersion
                    : ProtocolVersion;

                return new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = new JsonObject
                    {
                        ["protocolVersion"] = negotiateVersion,
                        ["capabilities"] = (JsonObject)Capabilities.DeepClone(),
                        ["serverInfo"] = (JsonObject)ServerInfo.DeepClone()
                    }
                };
            }

            case "server/discover":
                return new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = new JsonObject
                    {
                        ["protocolVersion"] = ProtocolVersion,
                        ["capabilities"] = (JsonObject)Capabilities.DeepClone(),
                        ["serverInfo"] = (JsonObject)ServerInfo.DeepClone(),
                        ["tools"] = CloneTools()
                    }
                };

            case "tools/list":
                return new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = new JsonObject
                    {
                        ["tools"] = CloneTools()
                    }
                };

            case "tools/call":
            {
                var name = prm.ValueKind == JsonValueKind.Object && prm.TryGetProperty("name", out var n)
                    ? n.GetString() ?? ""
                    : "";
                JsonElement? args = null;
                if (prm.ValueKind == JsonValueKind.Object && prm.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object)
                    args = a.Clone();

                string text;
                try
                {
                    text = await tools.ExecuteReadToolAsync(name, args);
                }
                catch (Exception ex)
                {
                    text = "{\"error\":\"" + ex.Message.Replace("\"", "\\\"") + "\"}";
                }

                var content = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = text }
                };
                return new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = new JsonObject { ["content"] = content, ["isError"] = false }
                };
            }

            case "notifications/initialized":
            case "notifications/cancelled":
            case "notifications/toolsListChanged":
            case "ping":
                return null;

            default:
                return new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["error"] = new JsonObject
                    {
                        ["code"] = -32601,
                        ["message"] = $"Method not found: {method}"
                    }
                };
        }
    }

    private static async Task WriteJsonRpcErrorAsync(HttpContext ctx, JsonNode? id, int code, string message, bool wantsSse, JsonObject? data = null)
    {
        var errorObj = new JsonObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null)
            errorObj["data"] = data;

        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = errorObj
        };

        if (wantsSse)
        {
            await WriteSseResponseAsync(ctx, envelope);
        }
        else
        {
            ctx.Response.ContentType = "application/json";
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsync(envelope.ToJsonString(), ctx.RequestAborted);
        }
    }

    private static JsonObject Tool(string name, string description, JsonObject? properties = null) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties ?? new JsonObject(),
            ["required"] = new JsonArray()
        }
    };

    private sealed class ForwardingLoggerProvider(ILogger<McpServerHost> host) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => host.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                host.Log(logLevel, eventId, "[McpServer] {Message}", formatter(state, exception));
        }
    }
}