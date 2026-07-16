namespace Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;

public interface IWafRuleChecker
{
    WafCheckResult Check(WafCheckContext context);
}
