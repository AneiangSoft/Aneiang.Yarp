namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// WAF target to check.
/// </summary>
public enum WafTarget
{
    /// <summary>Request URI path.</summary>
    Uri,
    /// <summary>Query string parameters.</summary>
    QueryString,
    /// <summary>Request body.</summary>
    RequestBody,
    /// <summary>Request headers.</summary>
    Header
}
