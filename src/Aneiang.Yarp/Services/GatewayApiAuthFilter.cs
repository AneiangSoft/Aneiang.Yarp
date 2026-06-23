using System.Text;
using Aneiang.Yarp.Controllers;
using Aneiang.Yarp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Services;

internal sealed class GatewayApiAuthFilter : IAsyncAuthorizationFilter
{
    private readonly GatewayApiAuthOptions _options;

    public GatewayApiAuthFilter(IOptions<GatewayApiAuthOptions> options) => _options = options.Value;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor { ControllerTypeInfo: var typeInfo } ||
            typeInfo.AsType() != typeof(GatewayConfigController))
            return Task.CompletedTask;

        var request = context.HttpContext.Request;
        var ok = _options.Mode switch
        {
            GatewayApiAuthMode.None => true,
            GatewayApiAuthMode.BasicAuth => ValidateBasic(request),
            GatewayApiAuthMode.ApiKey => ValidateApiKey(request),
            _ => false
        };

        if (!ok)
            context.Result = new JsonResult(new { code = 401, message = "Unauthorized" }) { StatusCode = StatusCodes.Status401Unauthorized };

        return Task.CompletedTask;
    }

    private bool ValidateBasic(HttpRequest request)
    {
        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
            return false;

        var header = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..]));
            var idx = decoded.IndexOf(':');
            return idx >= 0
                && string.Equals(decoded[..idx], _options.Username, StringComparison.Ordinal)
                && string.Equals(decoded[(idx + 1)..], _options.Password, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private bool ValidateApiKey(HttpRequest request)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey)) return false;

        if (request.Headers.TryGetValue(_options.ApiKeyHeaderName, out var headerValues)
            && headerValues.Any(v => string.Equals(v, _options.ApiKey, StringComparison.Ordinal)))
            return true;

        return _options.AllowApiKeyInQuery
            && request.Query.TryGetValue("api-key", out var queryValues)
            && queryValues.Any(v => string.Equals(v, _options.ApiKey, StringComparison.Ordinal));
    }
}
