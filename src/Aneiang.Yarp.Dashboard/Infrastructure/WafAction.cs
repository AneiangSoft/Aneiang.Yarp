namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// WAF action when rule matches.
/// </summary>
public enum WafAction
{
    /// <summary>Log the violation only.</summary>
    Log,
    /// <summary>Block the request (403 Forbidden).</summary>
    Block
}
