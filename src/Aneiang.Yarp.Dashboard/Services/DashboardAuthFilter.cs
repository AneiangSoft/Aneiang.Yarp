using Aneiang.Yarp.Dashboard.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Async auth filter for Dashboard controllers.
/// Skips login actions. Redirects browser to login; returns 401 JSON for API calls.
/// Uses IDashboardAuthorizationService for unified authorization check.
/// </summary>
internal sealed class DashboardAuthFilter : IAsyncAuthorizationFilter
{
    private readonly IDashboardAuthorizationService _authService;
    private readonly string _routePrefix;

    public DashboardAuthFilter(IDashboardAuthorizationService authService, string routePrefix)
    {
        _authService = authService;
        _routePrefix = routePrefix;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Only apply to Dashboard controllers
        if (context.ActionDescriptor is not ControllerActionDescriptor { ControllerTypeInfo: var ci })
            return;

        var controllerType = ci.AsType();
        if (controllerType != typeof(DashboardController) && controllerType != typeof(ConfigManagementController))
            return;

        // Skip login actions - they are public
        var actionName = ((ControllerActionDescriptor)context.ActionDescriptor).ActionName;
        if (string.Equals(actionName, "Login", StringComparison.OrdinalIgnoreCase))
            return;

        // Use authorization service for unified check
        if (await _authService.IsAuthorizedAsync(context.HttpContext)) return;

        var request = context.HttpContext.Request;
        var isApi = request.Headers["X-Requested-With"] == "XMLHttpRequest"
            || (request.Headers["Accept"].FirstOrDefault()?.Contains("application/json") == true)
            || request.Path.StartsWithSegments($"/{_routePrefix}/api");

        if (isApi)
            context.Result = new JsonResult(new { code = 401, message = "Unauthorized" }) { StatusCode = 401 };
        else
            context.Result = new RedirectResult($"/{_routePrefix}/login");
    }
}
