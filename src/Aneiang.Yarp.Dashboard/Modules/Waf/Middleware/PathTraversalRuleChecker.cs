using System.Text.RegularExpressions;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;

/// <summary>
/// Detects path traversal attacks: raw ../ and URL-encoded variants (%2e%2e/, %252e%252e).
/// Checks both the raw request path and the decoded query string.
/// </summary>
public sealed class PathTraversalRuleChecker : IWafRuleChecker
{
    public static readonly PathTraversalRuleChecker Instance = new();

    /// <summary>
    /// Path traversal: raw ../ and URL-encoded variants (%2e%2e/, %252e%252e).
    /// Simple alternation with no quantifiers — inherently safe.
    /// </summary>
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
