using System.Text.RegularExpressions;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;

public sealed class SqlInjectionRuleChecker : IWafRuleChecker
{
    public static readonly SqlInjectionRuleChecker Instance = new();

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex KeywordPattern = new(
        @"(?i)(?>\b(?:SELECT|INSERT|UPDATE|DELETE|DROP|UNION|EXEC|EXECUTE|XP_|SP_))" +
        @"|(?>\B--|\B/\*|\*/)",
        RegexOptions.Compiled,
        RegexTimeout);

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
