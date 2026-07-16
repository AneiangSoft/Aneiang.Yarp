using System.Text.RegularExpressions;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;

public sealed class XssRuleChecker : IWafRuleChecker
{
    public static readonly XssRuleChecker Instance = new();

    private static readonly Regex Pattern = new(
        @"(?i)<script[^>]*>|</script>|javascript:|data:text/html|<iframe[^>]*>|on(?>\w+)(?>\s*)=",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(50));

    public WafCheckResult Check(WafCheckContext context)
    {
        if (!context.Options.EnableXssDetection) return WafCheckResult.Passed;

        if (!string.IsNullOrEmpty(context.DecodedQueryString) && Pattern.IsMatch(context.DecodedQueryString))
            return WafCheckResult.Blocked("XssBlocked", Truncate(context.DecodedQueryString));

        if (!string.IsNullOrEmpty(context.DecodedBodyText) && Pattern.IsMatch(context.DecodedBodyText))
            return WafCheckResult.Blocked("XssBlocked", Truncate(context.DecodedBodyText));

        return WafCheckResult.Passed;
    }

    private static string Truncate(string value, int maxLength = 200) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
