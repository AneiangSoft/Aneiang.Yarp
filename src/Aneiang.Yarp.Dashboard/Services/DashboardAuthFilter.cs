using Aneiang.Yarp.Dashboard.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Async auth filter for DashboardController.
/// Skips login actions. Redirects browser to login; returns 401 JSON for API calls.
/// </summary>
internal sealed class DashboardAuthFilter : IAsyncAuthorizationFilter
{
    private readonly Func<HttpContext, Task<bool>> _checkAsync;
    private readonly string _routePrefix;

    public DashboardAuthFilter(Func<HttpContext, Task<bool>> checkAsync, string routePrefix)
    {
        _checkAsync = checkAsync;
        _routePrefix = routePrefix;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Only apply to DashboardController actions
        if (context.ActionDescriptor is not ControllerActionDescriptor
            { ControllerTypeInfo: var ci } || ci.AsType() != typeof(DashboardController))
            return;

        // Skip login actions - they are public
        var actionName = ((ControllerActionDescriptor)context.ActionDescriptor).ActionName;
        if (string.Equals(actionName, "Login", StringComparison.OrdinalIgnoreCase))
            return;

        if (await _checkAsync(context.HttpContext)) return;

        var request = context.HttpContext.Request;
        var isApi = request.Headers["X-Requested-With"] == "XMLHttpRequest"
            || (request.Headers["Accept"].FirstOrDefault()?.Contains("application/json") == true);

        if (isApi)
            context.Result = new JsonResult(new { code = 401, message = "Unauthorized" }) { StatusCode = 401 };
        else
            context.Result = new RedirectResult($"/{_routePrefix}/login");
    }
}
