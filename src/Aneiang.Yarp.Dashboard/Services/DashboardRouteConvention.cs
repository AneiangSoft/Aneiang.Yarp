using Aneiang.Yarp.Dashboard.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>Prepends the dashboard route prefix to all Dashboard assembly controllers.</summary>
/// <remarks>
/// Controllers that already have a full route prefix on the controller-level [Route] attribute
/// (e.g. <c>[Route("apigateway/api/config")]</c>) are excluded from prefix prepending.
/// </remarks>
internal sealed class DashboardRouteConvention : IApplicationModelConvention
{
    private readonly string _prefix;
    public DashboardRouteConvention(string prefix) => _prefix = prefix;

    public void Apply(ApplicationModel application)
    {
        foreach (var ctrl in application.Controllers)
        {
            // Only process controllers from the Dashboard assembly
            if (ctrl.ControllerType.Assembly != typeof(DashboardController).Assembly)
                continue;

            // Skip controllers that have a controller-level route already containing the prefix
            // (e.g. [Route("apigateway/api/config")] should not get double-prefixed)
            var controllerRoute = ctrl.Selectors
                .FirstOrDefault(s => s.AttributeRouteModel?.Template != null)?
                .AttributeRouteModel?.Template ?? "";

            // If the controller already has a route that starts with a known prefix segment,
            // skip it to avoid double-prefixing
            if (controllerRoute.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var action in ctrl.Actions)
            {
                foreach (var selector in action.Selectors)
                {
                    if (selector.AttributeRouteModel == null) continue;
                    var template = selector.AttributeRouteModel.Template ?? "";
                    selector.AttributeRouteModel.Template = template.StartsWith("/")
                        ? _prefix + template
                        : _prefix + "/" + template;
                }
            }
        }
    }
}
