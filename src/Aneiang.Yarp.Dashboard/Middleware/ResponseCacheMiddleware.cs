using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Dashboard.Middleware;

/// <summary>
/// Response caching middleware for proxy requests.
/// Caches GET/HEAD responses in memory with configurable TTL.
/// Respects Cache-Control headers from downstream services.
/// Per-route configuration via metadata: "ResponseCache:Ttl", "ResponseCache:Enabled".
/// </summary>
public sealed class ResponseCacheMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IResponseCacheService _cache;
    private readonly ResponseCacheOptions _options;
    private readonly ILogger<ResponseCacheMiddleware> _logger;

    public ResponseCacheMiddleware(
        RequestDelegate next,
        IResponseCacheService cache,
        IOptions<ResponseCacheOptions> options,
        ILogger<ResponseCacheMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        if (!_options.CacheableMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var routeId = proxyFeature?.Route?.Config?.RouteId;
        var metadata = proxyFeature?.Route?.Config?.Metadata;

        if (!IsCacheEnabledForRoute(metadata))
        {
            await _next(context);
            return;
        }

        var cacheKey = BuildCacheKey(context, routeId);

        if (_cache.TryGet(cacheKey, out var body, out var headers, out var statusCode))
        {
            context.Response.StatusCode = statusCode;
            foreach (var (key, value) in headers)
                context.Response.Headers[key] = value;
            context.Response.Headers["X-Cache"] = "HIT";
            await context.Response.Body.WriteAsync(body);
            _logger.LogDebug("Cache HIT for {Key}", cacheKey);
            return;
        }

        context.Response.Headers["X-Cache"] = "MISS";

        var originalBody = context.Response.Body;
        using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        await _next(context);

        context.Response.Body = originalBody;

        if (_options.CacheableStatusCodes.Contains(context.Response.StatusCode))
        {
            var ttl = GetTtlForRoute(routeId, metadata, context.Response);
            var responseBody = responseBuffer.ToArray();

            if (ttl > TimeSpan.Zero && responseBody.Length <= _options.MaxBodySize)
            {
                var responseHeaders = context.Response.Headers
                    .Where(h => !h.Key.Equals("X-Cache", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(h => h.Key, h => h.Value.ToString());

                _cache.Set(cacheKey, responseBody, responseHeaders, context.Response.StatusCode, ttl);
                _logger.LogDebug("Cached response for {Key} (TTL: {Ttl}s)", cacheKey, ttl.TotalSeconds);
            }
        }

        responseBuffer.Seek(0, SeekOrigin.Begin);
        await responseBuffer.CopyToAsync(originalBody);
    }

    private string BuildCacheKey(HttpContext context, string? routeId)
    {
        var components = new List<string> { routeId ?? "unknown" };

        if (_options.CacheKeyComponents.Contains("url"))
            components.Add(context.Request.Path.Value ?? "");

        if (_options.CacheKeyComponents.Contains("query"))
            components.Add(context.Request.QueryString.Value ?? "");

        if (_options.CacheKeyComponents.Contains("headers") && _options.VaryByHeaders != null)
        {
            foreach (var header in _options.VaryByHeaders.OrderBy(h => h, StringComparer.OrdinalIgnoreCase))
            {
                if (context.Request.Headers.TryGetValue(header, out var value))
                    components.Add($"{header}={value}");
            }
        }

        return string.Join("|", components);
    }

    private bool IsCacheEnabledForRoute(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata != null && metadata.TryGetValue("ResponseCache:Enabled", out var enabled))
        {
            return !bool.TryParse(enabled, out var isEnabled) || isEnabled;
        }
        return true;
    }

    private TimeSpan GetTtlForRoute(string? routeId, IReadOnlyDictionary<string, string>? metadata, HttpResponse response)
    {
        if (metadata != null && metadata.TryGetValue("ResponseCache:Ttl", out var ttlStr)
            && int.TryParse(ttlStr, out var ttlSeconds))
        {
            return TimeSpan.FromSeconds(Math.Min(ttlSeconds, (int)_options.MaxTtl.TotalSeconds));
        }

        if (routeId != null && _options.RouteOverrides != null)
        {
            foreach (var kvp in _options.RouteOverrides)
            {
                if (routeId.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
        }

        if (response.Headers.TryGetValue("Cache-Control", out var cacheControl))
        {
            var cc = cacheControl.ToString();
            if (cc.Contains("no-store", StringComparison.OrdinalIgnoreCase) ||
                cc.Contains("private", StringComparison.OrdinalIgnoreCase))
            {
                return TimeSpan.Zero;
            }

            var maxAgeMatch = System.Text.RegularExpressions.Regex.Match(cc, @"max-age=(\d+)");
            if (maxAgeMatch.Success && int.TryParse(maxAgeMatch.Groups[1].Value, out var maxAge))
            {
                return TimeSpan.FromSeconds(Math.Min(maxAge, (int)_options.MaxTtl.TotalSeconds));
            }
        }

        return _options.DefaultTtl;
    }
}
