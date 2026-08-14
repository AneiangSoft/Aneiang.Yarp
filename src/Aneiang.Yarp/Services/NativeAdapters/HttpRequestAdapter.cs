using Aneiang.Yarp.Storage.Entities;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace Aneiang.Yarp.Services;

/// <summary>Cluster HTTP request adapter: compiles to YARP native HttpRequest field.</summary>
public static class HttpRequestAdapter
{
    public const string PluginId = "native.cluster.http-request";

    public static NativePluginAdapterDescriptor Descriptor { get; } = new(PluginId, "Cluster HTTP Request", PluginBindingScope.Cluster);

    public static ClusterConfig Apply(ClusterConfig cluster, ClusterHttpRequestConfig value) =>
        cluster with { HttpRequest = ToHttpRequest(value) };

    private static ForwarderRequestConfig ToHttpRequest(ClusterHttpRequestConfig value)
    {
        if (value.ActivityTimeout <= TimeSpan.Zero)
            throw new ArgumentException("ActivityTimeout must be greater than zero.");
        return new ForwarderRequestConfig
        {
            ActivityTimeout = value.ActivityTimeout,
            Version = value.Version,
            VersionPolicy = value.VersionPolicy,
            AllowResponseBuffering = value.AllowResponseBuffering
        };
    }
}

/// <summary>Configuration model for <see cref="HttpRequestAdapter"/>.</summary>
public sealed class ClusterHttpRequestConfig
{
    public TimeSpan? ActivityTimeout { get; init; }
    public Version? Version { get; init; }
    public HttpVersionPolicy? VersionPolicy { get; init; }
    public bool? AllowResponseBuffering { get; init; }
}
