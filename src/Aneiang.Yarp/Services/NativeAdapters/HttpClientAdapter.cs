using System.Security.Authentication;
using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>Cluster HTTP client adapter: compiles to YARP native HttpClient field.</summary>
public static class HttpClientAdapter
{
    public const string PluginId = "native.cluster.http-client";

    public static NativePluginAdapterDescriptor Descriptor { get; } = new(PluginId, "Cluster HTTP Client", PluginBindingScope.Cluster);

    public static ClusterConfig Apply(ClusterConfig cluster, ClusterHttpClientConfig value) =>
        cluster with { HttpClient = ToHttpClient(value) };

    private static HttpClientConfig ToHttpClient(ClusterHttpClientConfig value)
    {
        if (value.MaxConnectionsPerServer <= 0)
            throw new ArgumentException("MaxConnectionsPerServer must be greater than zero.");
        return new HttpClientConfig
        {
            SslProtocols = value.SslProtocols,
            DangerousAcceptAnyServerCertificate = value.DangerousAcceptAnyServerCertificate,
            MaxConnectionsPerServer = value.MaxConnectionsPerServer,
            EnableMultipleHttp2Connections = value.EnableMultipleHttp2Connections,
            RequestHeaderEncoding = value.RequestHeaderEncoding,
            ResponseHeaderEncoding = value.ResponseHeaderEncoding,
            WebProxy = value.WebProxy == null ? null : new WebProxyConfig
            {
                Address = value.WebProxy.Address,
                BypassOnLocal = value.WebProxy.BypassOnLocal,
                UseDefaultCredentials = value.WebProxy.UseDefaultCredentials
            }
        };
    }
}

/// <summary>Configuration model for <see cref="HttpClientAdapter"/>.</summary>
public sealed class ClusterHttpClientConfig
{
    public SslProtocols? SslProtocols { get; init; }
    public bool? DangerousAcceptAnyServerCertificate { get; init; }
    public int? MaxConnectionsPerServer { get; init; }
    public bool? EnableMultipleHttp2Connections { get; init; }
    public string? RequestHeaderEncoding { get; init; }
    public string? ResponseHeaderEncoding { get; init; }
    public NativeWebProxyConfig? WebProxy { get; init; }
}

public sealed class NativeWebProxyConfig
{
    public Uri? Address { get; init; }
    public bool? BypassOnLocal { get; init; }
    public bool? UseDefaultCredentials { get; init; }
}
