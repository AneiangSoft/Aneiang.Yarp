using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Plugin.Cache;

public sealed class ResponseCacheMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly GatewayPluginExecutionPlanProvider _plans;

    public ResponseCacheMiddleware(RequestDelegate next, IMemoryCache cache, GatewayPluginExecutionPlanProvider plans)
    {
        _next = next;
        _cache = cache;
        _plans = plans;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var routeId = context.Features.Get<IReverseProxyFeature>()?.Route?.Config?.RouteId;
        if (context.Request.Method is not ("GET" or "HEAD") || string.IsNullOrWhiteSpace(routeId) ||
            !_plans.Current.ResponseCacheByRoute.TryGetValue(routeId, out var config) || !config.Enabled)
        {
            await _next(context);
            return;
        }

        var query = config.VaryByQuery ? context.Request.QueryString.Value : null;
        var headerParts = config.VaryHeaders.Select(name => $"{name}={context.Request.Headers[name]}");
        var key = $"plugin-cache:{routeId}:{context.Request.Path}:{query}:{string.Join('|', headerParts)}";
        if (_cache.TryGetValue(key, out CachedResponse? cached) && cached != null)
        {
            context.Response.StatusCode = cached.StatusCode;
            foreach (var header in cached.Headers) context.Response.Headers[header.Key] = header.Value;
            context.Response.Headers.ContentLength = cached.Body.Length;
            if (context.Request.Method != "HEAD") await context.Response.Body.WriteAsync(cached.Body, context.RequestAborted);
            return;
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);
            var body = buffer.ToArray();
            buffer.Position = 0;
            await buffer.CopyToAsync(original, context.RequestAborted);
            if (body.Length <= config.MaxBodyBytes && config.CacheStatusCodes.Contains(context.Response.StatusCode) &&
                !context.Response.Headers.ContainsKey("Set-Cookie") && !context.Response.Headers.CacheControl.ToString().Contains("no-store", StringComparison.OrdinalIgnoreCase))
            {
                var headers = context.Response.Headers.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                _cache.Set(key, new CachedResponse(context.Response.StatusCode, headers, body), TimeSpan.FromSeconds(config.TtlSeconds));
            }
        }
        finally
        {
            context.Response.Body = original;
        }
    }

    private sealed record CachedResponse(int StatusCode, IReadOnlyDictionary<string, string> Headers, byte[] Body);
}
