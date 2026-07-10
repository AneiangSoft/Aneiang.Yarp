using System.Text.RegularExpressions;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;

/// <summary>
/// Detects SQL injection attacks in the decoded query string and request body.
/// Uses two complementary regex patterns:
/// <list type="bullet">
///   <item>Keyword pattern: SELECT/INSERT/UPDATE/DELETE/DROP/UNION/EXEC + comment markers</item>
///   <item>Value pattern: <c>' OR '1'='1</c> and <c>; DROP TABLE</c> style attacks</item>
/// </list>
/// Atomic groups prevent catastrophic backtracking on long inputs.
/// </summary>
public sealed class SqlInjectionRuleChecker : IWafRuleChecker
{
    public static readonly SqlInjectionRuleChecker Instance = new();

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    /// <summary>SQL keyword + comment injection detection.</summary>
    private static readonly Regex KeywordPattern = new(
        @"(?i)(?>\b(?:SELECT|INSERT|UPDATE|DELETE|DROP|UNION|EXEC|EXECUTE|XP_|SP_))" +
        @"|(?>\B--|\B/\*|\*/)",
        RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>SQL injection value pattern: <c>' OR '1'='1</c> and <c>; DROP TABLE</c>.</summary>
    private static readonly Regex ValuePattern = new(
        @"(?i)'(?>\s*(?:OR|AND)\s*)['""]?\w+['""]?(?>\s*)(?:=|LIKE|<|>)" +
        @"|;(?>\s*)(?:DROP|DELETE|INSERT|UPDATE)(?:\b|$)",
        RegexOptions.Compiled,
        RegexTimeout);

    public WafCheckResult Check(WafCheckContext context)
    {
        if (!context.Options.EnableSqlInjectionDetection) return WafCheckResult.Passed;

        // Scan decoded query string
        if (!string.IsNullOrEmpty(context.DecodedQueryString))
        {
            var r = MatchText(context.DecodedQueryString);
            if (r.IsBlocked) return r;
        }

        // Scan decoded body text
        if (!string.IsNullOrEmpty(context.DecodedBodyText))
        {
            var r = MatchText(context.DecodedBodyText);
            if (r.IsBlocked) return r;
        }

        return WafCheckResult.Passed;
    }

    private static WafCheckResult MatchText(string text)
    {
        if (KeywordPattern.IsMatch(text))
            return WafCheckResult.Blocked("SqlInjectionBlocked", Truncate(text));
        if (ValuePattern.IsMatch(text))
            return WafCheckResult.Blocked("SqlInjectionValueBlocked", Truncate(text));
        return WafCheckResult.Passed;
    }

    private static string Truncate(string value, int maxLength = 200) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
