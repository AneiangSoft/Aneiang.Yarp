namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// WAF rule type.
/// </summary>
public enum WafRuleType
{
    /// <summary>Regular expression match.</summary>
    Regex,
    /// <summary>SQL injection detection.</summary>
    SqlInjection,
    /// <summary>XSS script detection.</summary>
    Xss,
    /// <summary>Path traversal detection.</summary>
    PathTraversal
}
