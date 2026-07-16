using System.Buffers;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Middleware;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Modules.Waf.Services;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Infrastructure;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;

public sealed class WafMiddleware : GatewayMiddlewareBase
{
    private readonly ILogger<WafMiddleware> _logger;
    private readonly WafOptions _wafOptions;
    private readonly IWafSettingsPersistenceService? _wafPersistence;
    private readonly INotificationService _notificationService;

    private static readonly IWafRuleChecker[] Checkers =
    [
        IpAccessRuleChecker.Instance,
        RequestSizeRuleChecker.Instance,
        PathTraversalRuleChecker.Instance,
        SqlInjectionRuleChecker.Instance,
        XssRuleChecker.Instance,
    ];

    public WafMiddleware(
        RequestDelegate next,
        ILogger<WafMiddleware> logger,
        IOptions<WafOptions> wafOptions,
        IOptions<DashboardOptions> dashOptions,
        IGatewayPluginManager pluginManager,
        IWafSettingsPersistenceService? wafPersistence = null,
        INotificationService? notificationService = null)
        : base(next, dashOptions, pluginManager)
    {
        _logger = logger;
        _wafOptions = wafOptions.Value;
        var prefix = _wafOptions.DashboardRoutePrefix;
        if (string.IsNullOrWhiteSpace(prefix) || prefix == "apigateway")
            prefix = dashOptions.Value.RoutePrefix;
        DashPrefix = "/" + prefix.Trim('/');
        _wafPersistence = wafPersistence;
        _notificationService = notificationService ?? NullNotificationService.Instance;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var opts = ResolveEffectiveOptions();
        var path = context.Request.Path.Value ?? "";

        // Fast-path: WAF disabled globally or per-route
        bool globallyEnabled = opts.Enabled && IsPluginEnabled("waf");
        if (!globallyEnabled)
        {
            var routeMeta = context.Features.Get<IReverseProxyFeature>()?.Route?.Config?.Metadata;
            if (routeMeta == null || !routeMeta.TryGetValue("Waf:Enabled", out var v) ||
                !bool.TryParse(v, out var parsed) || !parsed)
            {
                await Next(context);
                return;
            }
        }

        // Skip dashboard static content and UI routes
        if (IsDashboardRequest(context))
        {
            await Next(context);
            return;
        }

        // Build shared context with decoded inputs
        var rawPath = GetRawRequestPath(context);
        var decodedQuery = DecodeQuery(context.Request.QueryString.Value);

        var wafContext = new WafCheckContext
        {
            HttpContext = context,
            Options = opts,
            DecodedQueryString = decodedQuery,
            RawRequestPath = rawPath
        };

        // Decode body for POST/PUT/PATCH if injection checkers are active
        if ((opts.EnableSqlInjectionDetection || opts.EnableXssDetection) &&
            context.Request.ContentLength > 0 &&
            (HttpMethods.IsPost(context.Request.Method) ||
             HttpMethods.IsPut(context.Request.Method) ||
             HttpMethods.IsPatch(context.Request.Method)))
        {
            wafContext.DecodedBodyText = await ReadAndDecodeBodyAsync(context);
        }

        // Run checker chain
        foreach (var checker in Checkers)
        {
            var result = checker.Check(wafContext);
            if (result.IsBlocked)
            {
                LogWafBlock(context, result.EventType!, result.Details);
                await BlockRequest(context, result.EventType switch
                {
                    "IpBlocked" => "Access denied",
                    "RequestSizeBlocked" => "Request entity too large",
                    "MalformedHeadersBlocked" => "Too many headers",
                    "UriTooLongBlocked" => "URI too long",
                    _ => "Blocked by security policy"
                });
                return;
            }
        }

        await Next(context);
    }

    private WafOptions ResolveEffectiveOptions()
    {
        var data = _wafPersistence?.Load();
        if (data == null) return _wafOptions;

        return new WafOptions
        {
            Enabled = data.Enabled,
            DashboardRoutePrefix = _wafOptions.DashboardRoutePrefix,
            IpWhitelist = data.IpWhitelist.Count > 0 ? data.IpWhitelist : _wafOptions.IpWhitelist,
            IpBlacklist = data.IpBlacklist.Count > 0 ? data.IpBlacklist : _wafOptions.IpBlacklist,
            MaxRequestBodySize = data.MaxRequestBodySize,
            MaxHeaderCount = data.MaxHeaderCount,
            MaxHeaderSize = data.MaxHeaderSize,
            EnableSqlInjectionDetection = data.EnableSqlInjectionDetection,
            EnableXssDetection = data.EnableXssDetection,
            EnablePathTraversalDetection = data.EnablePathTraversalDetection,
            EnableIpCheck = data.EnableIpCheck,
            EnableRequestSizeValidation = data.EnableRequestSizeValidation,
            ExtraScriptSources = _wafOptions.ExtraScriptSources
        };
    }

    private static string GetRawRequestPath(HttpContext context)
    {
        var raw = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestFeature>()?.RawTarget;
        if (string.IsNullOrEmpty(raw)) return context.Request.Path.Value ?? "";
        var qIdx = raw.IndexOf('?');
        return qIdx >= 0 ? raw[..qIdx] : raw;
    }

    private static string DecodeQuery(string? rawQuery)
    {
        if (string.IsNullOrEmpty(rawQuery)) return "";
        var trimmed = rawQuery.TrimStart('?');
        return string.IsNullOrEmpty(trimmed) ? "" : WebUtility.UrlDecode(trimmed);
    }

    private static async Task<string?> ReadAndDecodeBodyAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        try
        {
            using var reader = new StreamReader(
                context.Request.Body, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

            var maxScanBytes = (int)Math.Min(context.Request.ContentLength ?? 0, 100 * 1024);
            if (maxScanBytes <= 0) maxScanBytes = 4096;
            var buffer = ArrayPool<char>.Shared.Rent(maxScanBytes);
            try
            {
                var read = await reader.ReadAsync(buffer, 0, maxScanBytes);
                context.Request.Body.Position = 0;
                var text = new string(buffer, 0, read);
                // URL-decode form-urlencoded bodies
                if (context.Request.ContentType?.IndexOf("urlencoded", StringComparison.OrdinalIgnoreCase) >= 0)
                    text = WebUtility.UrlDecode(text);
                return text;
            }
            finally { ArrayPool<char>.Shared.Return(buffer); }
        }
        catch { context.Request.Body.Position = 0; return null; }
    }

    private void LogWafBlock(HttpContext context, string eventType, string? details)
    {
        var clientIp = ClientIpResolver.GetClientIp(context);
        var requestUri = context.Request.Path.Value + context.Request.QueryString.Value;

        _logger.LogWarning("WAF [{EventType}] from IP: {Ip}, Path: {Path}, Details: {Details}",
            eventType, clientIp, requestUri, details ?? "(hidden)");

        _notificationService.NotifyWafBlock(clientIp ?? "unknown", eventType, requestUri);
    }

    private static async Task BlockRequest(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "Forbidden", message, waf = true });
    }

}
