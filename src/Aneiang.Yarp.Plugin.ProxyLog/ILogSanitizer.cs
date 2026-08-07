using Aneiang.Yarp.Plugin.ProxyLog.Models;
using Microsoft.AspNetCore.Http;

namespace Aneiang.Yarp.Plugin.ProxyLog;

/// <summary>
/// Sanitizes sensitive information from log entries.
/// Interface defined in the plugin; implementation provided by Dashboard.
/// </summary>
public interface ILogSanitizer
{
    HeaderList? SanitizeHeaders(IHeaderDictionary? headers);
    string? SanitizeQueryString(string? queryString);
    string? SanitizeJsonBody(string? body);
    string? SanitizeBody(string? body, string? contentType);
    string? TruncateText(string? text, out bool truncated);
}
