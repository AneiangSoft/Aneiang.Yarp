using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Builds and stores proxy log entries. Extracted from YarpRequestCaptureMiddleware
/// to separate log entry construction from HTTP pipeline orchestration.
/// </summary>
public interface IProxyLogCapture
{
    /// <summary>
    /// Builds and stores both ProxyRequest and ProxyResponse log entries.
    /// </summary>
    void CaptureLogEntry(
        HttpContext context,
        IReverseProxyFeature? proxyFeature,
        string upstreamPath,
        string? routeId,
        string? clusterId,
        DateTime timestamp,
        TimeSpan elapsed,
        HeaderList? requestHeaders,
        string requestBody,
        bool requestBodyTruncated,
        string? responseBodyText,
        bool responseTruncated,
        HeaderList? responseHeaders,
        string? downstreamText,
        bool downstreamTruncated);
}
