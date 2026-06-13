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
    /// 从 "Kestrel:EndPoints" 配置节读取并配置
    /// </summary>
    private static bool ConfigureFromKestrelSection(
        IConfiguration configuration,
        KestrelServerOptions options,
        bool forceAnyIP)
    {
        var kestrelSection = configuration.GetSection("Kestrel:EndPoints");
        if (!kestrelSection.Exists())
        {
            return false;
        }

        var configured = false;
        foreach (var endpoint in kestrelSection.GetChildren())
        {
            var url = endpoint["Url"];
            if (string.IsNullOrEmpty(url))
            {
                continue;
            }

            if (TryParseAndConfigure(url, options, forceAnyIP))
            {
                configured = true;
            }
        }

        return configured;
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
            if (TryParseAndConfigure(url.Trim(), options, forceAnyIP))
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
            if (TryParseAndConfigure(url.Trim(), options, forceAnyIP))
            {
                configured = true;
            }
        }

        return configured;
    }

    /// <summary>
    /// 解析 URL 并配置 Kestrel 监听。HTTP 端点自动启用 Http1AndHttp2 以支持 gRPC。
    /// 对于纯 HTTP（非 TLS）端点，额外在 port+1 上开启 HTTP/2 only 端口给 gRPC
    /// （.NET 9 Kestrel 不支持在无 TLS 的端点上同时协商 HTTP/1.1 和 HTTP/2）。
    /// </summary>
    private static bool TryParseAndConfigure(
        string url,
        KestrelServerOptions options,
        bool forceAnyIP)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var port = uri.Port;
        var isHttps = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        var host = uri.Host;

        // 判断是否需要监听 0.0.0.0
        var shouldListenAnyIP = forceAnyIP ||
                               host is "localhost" or "127.0.0.1" or "0.0.0.0" or "::1";

        if (shouldListenAnyIP)
        {
            if (isHttps)
            {
                options.ListenAnyIP(port, listenOptions => listenOptions.UseHttps());
            }
            else
            {
                // Main port: HTTP/1.1 only (Dashboard, YARP proxy, REST API)
                options.ListenAnyIP(port, o => o.Protocols = HttpProtocols.Http1);
                // gRPC port: HTTP/2 only (h2c) — .NET 9 requires separate port for cleartext HTTP/2
                options.ListenAnyIP(port + 1, o => o.Protocols = HttpProtocols.Http2);
            }
            return true;
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
                options.Listen(ipAddress, port, o => o.Protocols = HttpProtocols.Http1);
                options.Listen(ipAddress, port + 1, o => o.Protocols = HttpProtocols.Http2);
            }
            return true;
        }

        return false;
    }
}
