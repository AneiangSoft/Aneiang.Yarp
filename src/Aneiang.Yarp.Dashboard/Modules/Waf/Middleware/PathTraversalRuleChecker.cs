using System.Text.RegularExpressions;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;

public sealed class PathTraversalRuleChecker : IWafRuleChecker
{
    public static readonly PathTraversalRuleChecker Instance = new();

    private static readonly Regex Pattern = new(
        @"(?i)(?>\.\.[/%5c\\])|(?>%2e%2e[%/%5c\\])|(?>%252e%252e)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(50));

    public WafCheckResult Check(WafCheckContext context)
    {
        if (!context.Options.EnablePathTraversalDetection) return WafCheckResult.Passed;

        // Check raw path (un-normalized — preserves ../ that ASP.NET might strip)
        if (Pattern.IsMatch(context.RawRequestPath))
            return WafCheckResult.Blocked("PathTraversalBlocked", context.RawRequestPath);

        // Check decoded query string
        if (!string.IsNullOrEmpty(context.DecodedQueryString) && Pattern.IsMatch(context.DecodedQueryString))
            return WafCheckResult.Blocked("PathTraversalInQueryBlocked", context.DecodedQueryString);

        return WafCheckResult.Passed;
    }
}
