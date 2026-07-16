using Microsoft.AspNetCore.Http;
using Aneiang.Yarp.Dashboard.Infrastructure;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;

public sealed class WafCheckContext
{
    public required HttpContext HttpContext { get; init; }
    public required WafOptions Options { get; init; }

    public required string DecodedQueryString { get; init; }

    public string? DecodedBodyText { get; set; }

    public required string RawRequestPath { get; init; }
}
