using Microsoft.AspNetCore.Http;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;

/// <summary>
/// Validates request size (Content-Length), header count/size, and URI length.
/// </summary>
public sealed class RequestSizeRuleChecker : IWafRuleChecker
{
    public static readonly RequestSizeRuleChecker Instance = new();

    public WafCheckResult Check(WafCheckContext context)
    {
        if (!context.Options.EnableRequestSizeValidation) return WafCheckResult.Passed;

        var req = context.HttpContext.Request;
        var opts = context.Options;

        // Content-Length
        if (req.ContentLength.HasValue && req.ContentLength.Value > opts.MaxRequestBodySize)
            return WafCheckResult.Blocked("RequestSizeBlocked");

        // Header count
        if (req.Headers.Count > opts.MaxHeaderCount)
            return WafCheckResult.Blocked("MalformedHeadersBlocked");

        // Individual header size
        foreach (var header in req.Headers)
        {
            if (header.Value.Count > 1 || header.Value.ToString().Length > opts.MaxHeaderSize)
                return WafCheckResult.Blocked("MalformedHeadersBlocked");
        }

        // URI length
        var path = context.HttpContext.Request.Path.Value ?? "";
        if (path.Length > 4096)
            return WafCheckResult.Blocked("UriTooLongBlocked");

        return WafCheckResult.Passed;
    }
}
