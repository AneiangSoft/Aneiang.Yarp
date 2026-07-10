using Microsoft.AspNetCore.Http;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;

/// <summary>
/// Shared context passed to all <see cref="IWafRuleChecker"/> implementations.
/// Middleware decodes query string and body once; checkers read the pre-computed values.
/// </summary>
public sealed class WafCheckContext
{
    public required HttpContext HttpContext { get; init; }
    public required WafOptions Options { get; init; }

    /// <summary>Decoded query string (empty when absent).</summary>
    public required string DecodedQueryString { get; init; }

    /// <summary>Decoded body text for POST/PUT/PATCH requests (null when not applicable).</summary>
    public string? DecodedBodyText { get; set; }

    /// <summary>Raw (un-normalized) request path used for path traversal detection.</summary>
    public required string RawRequestPath { get; init; }
}

/// <summary>
/// Result of a WAF rule check. <see cref="Passed"/> is the fast path; blocked results
/// carry the <see cref="EventType"/> and optional <see cref="Details"/> for event recording.
/// </summary>
public sealed class WafCheckResult
{
    public static readonly WafCheckResult Passed = new() { IsBlocked = false };

    public bool IsBlocked { get; init; }
    public string? EventType { get; init; }
    public string? Details { get; init; }

    public static WafCheckResult Blocked(string eventType, string? details = null) =>
        new() { IsBlocked = true, EventType = eventType, Details = details };
}

/// <summary>
/// A single WAF rule checker. Implementations should be stateless and thread-safe.
/// </summary>
public interface IWafRuleChecker
{
    /// <summary>
    /// Evaluate the request against this rule. Return <see cref="WafCheckResult.Passed"/>
    /// when the check passes, or <see cref="WafCheckResult.Blocked"/> to reject the request.
    /// </summary>
    WafCheckResult Check(WafCheckContext context);
}
