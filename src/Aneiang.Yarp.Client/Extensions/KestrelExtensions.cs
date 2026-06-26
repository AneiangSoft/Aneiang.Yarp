using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;

namespace Aneiang.Yarp.Extensions;

/// <summary>
/// Kestrel 自动配置扩展方法。支持 HTTP/1.1 + HTTP/2 双协议，确保 gRPC 可用。
/// </summary>
public static class KestrelExtensions
{
    /// <summary>
    /// 自动配置 Kestrel 监听 0.0.0.0，支持跨机器访问
    /// 自动检测并覆盖以下配置：
    /// 1. "Urls" 配置项
    /// 2. "Kestrel:EndPoints" 配置节
    /// 3. ASPNETCORE_URLS 环境变量
    /// </summary>
    /// <param name="builder">WebApplicationBuilder</param>
    /// <param name="forceAnyIP">是否强制监听 0.0.0.0（即使配置中是 localhost）</param>
    /// <returns>WebApplicationBuilder</returns>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.UseYarpKestrelAutoConfig();
    /// </code>
    /// </example>
    public static WebApplicationBuilder UseYarpKestrelAutoConfig(
        this WebApplicationBuilder builder,
        bool forceAnyIP = true)
    {
        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            ConfigureFromAllSources(context.Configuration, options, forceAnyIP);
        });

        return builder;
    }


    /// <summary>
    /// 自动配置 Kestrel 监听 0.0.0.0，支持跨机器访问
    /// 自动检测并覆盖以下配置：
    /// 1. "Urls" 配置项
    /// 2. "Kestrel:EndPoints" 配置节
    /// 3. ASPNETCORE_URLS 环境变量
    /// </summary>
    /// <param name="builder">WebApplicationBuilder</param>
    /// <param name="forceAnyIP">是否强制监听 0.0.0.0（即使配置中是 localhost）</param>
    /// <returns>WebApplicationBuilder</returns>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.UseYarpKestrelAutoConfig();
    /// </code>
    /// </example>
    public static IWebHostBuilder UseYarpKestrelAutoConfig(
        this IWebHostBuilder builder,
        bool forceAnyIP = true)
    {
        builder.ConfigureKestrel((context, options) =>
        {
            ConfigureFromAllSources(context.Configuration, options, forceAnyIP);
        });

        return builder;
    }

    /// <summary>
    /// Config key for explicit gRPC port override on the gateway.
    /// When not set, defaults to <c>mainPort + 1</c> in HTTP mode.
    /// </summary>
    private const string GrpcPortConfigKey = "Gateway:Grpc:Port";

    /// <summary>
    /// 从所有配置源读取并配置 Kestrel 端点
    /// </summary>
    private static void ConfigureFromAllSources(
        IConfiguration configuration,
        KestrelServerOptions options,
        bool forceAnyIP)
    {
        var configured = false;

        // 1. 从 "Kestrel:EndPoints" 配置节读取（优先级最高）
        configured |= ConfigureFromKestrelSection(configuration, options, forceAnyIP);

        // 2. 从 "Urls" 配置项读取
        if (!configured)
        {
            configured |= ConfigureFromUrls(configuration, options, forceAnyIP);
        }

        // 3. 从 ASPNETCORE_URLS 环境变量读取
        if (!configured)
        {
            configured |= ConfigureFromEnvironment(options, forceAnyIP);
        }

        // 4. 如果都没有配置，使用默认端口
        //    Port 5000: HTTP/1.1 (Dashboard, YARP) + Port 5001: HTTP/2 (gRPC)
        if (!configured)
        {
            options.ListenAnyIP(5000, o => o.Protocols = HttpProtocols.Http1);
            options.ListenAnyIP(5001, o => o.Protocols = HttpProtocols.Http2);
        }
    }

    /// <summary>
    /// Resolves the gRPC port from config (<c>Gateway:Grpc:Port</c>) or falls back to <c>mainPort + 1</c>.
    /// </summary>
    private static int ResolveGrpcPort(IConfiguration? configuration, int mainPort)
    {
        if (configuration != null)
        {
            var configuredPort = configuration.GetValue<int?>(GrpcPortConfigKey);
            if (configuredPort.HasValue)
                return configuredPort.Value;
        }
        return mainPort + 1;
    }

    /// <summary>
    /// 从 "Kestrel:EndPoints" 配置节读取并配置。
    /// 当 Kestrel:Endpoints 存在时，Kestrel 原生配置系统已经自动绑定这些端点，
    /// 此方法只需返回 true 表示"已配置"，不再手动添加 ListenAnyIP（否则会重复绑定导致 address already in use）。
    /// </summary>
    private static bool ConfigureFromKestrelSection(
        IConfiguration configuration,
        KestrelServerOptions options,
        bool forceAnyIP)
    {
        // Check both "EndPoints" (legacy) and "Endpoints" (standard) — .NET config is case-insensitive
        var kestrelSection = configuration.GetSection("Kestrel:EndPoints");
        if (!kestrelSection.Exists())
        {
            kestrelSection = configuration.GetSection("Kestrel:Endpoints");
        }
        if (!kestrelSection.Exists())
        {
            return false;
        }

        // Kestrel natively reads Kestrel:Endpoints from IConfiguration and binds the endpoints.
        // We must NOT call options.ListenAnyIP() here — that would create duplicate bindings.
        // Just return true to signal "configured" so that ConfigureFromAllSources doesn't
        // fall through to Urls/ASPNETCORE_URLS/default ports.
        return true;
    }

    /// <summary>
    /// 从 "Urls" 配置项读取并配置
    /// </summary>
    private static bool ConfigureFromUrls(
        IConfiguration configuration,
        KestrelServerOptions options,
        bool forceAnyIP)
    {
        var urls = configuration["Urls"];
        if (string.IsNullOrEmpty(urls))
        {
            return false;
        }

        var configured = false;
        foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseAndConfigure(url.Trim(), options, forceAnyIP, configuration))
            {
                configured = true;
            }
        }

        return configured;
    }

    /// <summary>
    /// 从 ASPNETCORE_URLS 环境变量读取并配置
    /// </summary>
    private static bool ConfigureFromEnvironment(
        KestrelServerOptions options,
        bool forceAnyIP)
    {
        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (string.IsNullOrEmpty(urls))
        {
            return false;
        }

        var configured = false;
        foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseAndConfigure(url.Trim(), options, forceAnyIP, configuration: null))
            {
                configured = true;
            }
        }

        return configured;
    }

    /// <summary>
    /// 解析 URL 并配置 Kestrel 监听。
    /// 对于纯 HTTP（非 TLS）端点，额外开启 HTTP/2 only 端口给 gRPC
    /// （.NET 9 Kestrel 不支持在无 TLS 的端点上同时协商 HTTP/1.1 和 HTTP/2）。
    /// gRPC 端口可通过 <c>Gateway:Grpc:Port</c> 显式指定，否则默认为主端口+1。
    /// </summary>
    private static bool TryParseAndConfigure(
        string url,
        KestrelServerOptions options,
        bool forceAnyIP,
        IConfiguration? configuration)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var port = uri.Port;
        var isHttps = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        var host = uri.Host;

        // When the user has explicitly declared 2+ Kestrel:Endpoints (multi-port / split mode),
        // we skip the auto-injected gRPC port. Each user-declared port should be honored
        // exactly as configured, with no implicit +1 offset.
        var hasExplicitMultiEndpoints = configuration != null
            && configuration.GetSection("Kestrel:Endpoints").GetChildren().Count() >= 2;
        var skipAutoGrpc = hasExplicitMultiEndpoints;

        // Split / multi-endpoint mode must honor the configured bind address.
        // Only legacy single-URL mode keeps the old forceAnyIP behavior for backward compatibility.
        var shouldListenAnyIP = IsAnyAddressHost(host) || (!hasExplicitMultiEndpoints && forceAnyIP);

        if (shouldListenAnyIP)
        {
            if (isHttps)
            {
                options.ListenAnyIP(port, listenOptions => listenOptions.UseHttps());
            }
            else
            {
                if (skipAutoGrpc)
                {
                    // Multi-endpoint mode: bind only the declared port, no auto gRPC.
                    options.ListenAnyIP(port, o => o.Protocols = HttpProtocols.Http1AndHttp2);
                }
                else
                {
                    var grpcPort = ResolveGrpcPort(configuration, port);
                    // Main port: HTTP/1.1 only (Dashboard, YARP proxy, REST API)
                    options.ListenAnyIP(port, o => o.Protocols = HttpProtocols.Http1);
                    // gRPC port: HTTP/2 only (h2c) — .NET 9 requires separate port for cleartext HTTP/2
                    options.ListenAnyIP(grpcPort, o => o.Protocols = HttpProtocols.Http2);
                }
            }
            return true;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            host = IPAddress.Loopback.ToString();
        }

        // 如果指定了特定 IP，监听该 IP
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            if (isHttps)
            {
                options.Listen(ipAddress, port, listenOptions => listenOptions.UseHttps());
            }
            else
            {
                if (skipAutoGrpc)
                {
                    // Multi-endpoint mode: bind only the declared port, no auto gRPC.
                    options.Listen(ipAddress, port, o => o.Protocols = HttpProtocols.Http1AndHttp2);
                }
                else
                {
                    var grpcPort = ResolveGrpcPort(configuration, port);
                    options.Listen(ipAddress, port, o => o.Protocols = HttpProtocols.Http1);
                    options.Listen(ipAddress, grpcPort, o => o.Protocols = HttpProtocols.Http2);
                }
            }
            return true;
        }

        return false;
    }

    private static bool IsAnyAddressHost(string host) =>
        string.IsNullOrWhiteSpace(host) || host is "0.0.0.0" or "*" or "+" or "::" or "[::]";
}
