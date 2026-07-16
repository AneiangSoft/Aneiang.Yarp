using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;

namespace Aneiang.Yarp.Extensions;

public static class KestrelExtensions
{
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

    private const string GrpcPortConfigKey = "Gateway:Grpc:Port";

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
