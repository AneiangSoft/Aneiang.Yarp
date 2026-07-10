using Microsoft.AspNetCore.Http;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Determines whether a request should be skipped from logging entirely
/// (Dashboard, gRPC, static files) and whether a route passes filtering rules
/// (whitelist, blacklist, min log level, errors-only).
/// </summary>
public interface ILogFilter
{
    /// <summary>Whether proxy logging is globally enabled.</summary>
    bool IsLoggingEnabled { get; }

    /// <summary>
    /// Returns <c>true</c> if the request should be completely skipped from logging
    /// (Dashboard paths, gRPC, static files).
    /// </summary>
    bool IsSkippedRequest(HttpContext context);

    /// <summary>
    /// Returns <c>true</c> if the response passes all filtering rules
    /// (min log level, errors-only, route whitelist/blacklist, sampling).
    /// </summary>
    bool ShouldLog(HttpContext context, string? routeId);
}
