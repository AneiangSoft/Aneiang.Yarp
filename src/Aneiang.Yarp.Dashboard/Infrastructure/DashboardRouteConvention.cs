
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>Prepends the dashboard route prefix to all Dashboard assembly controllers.</summary>
/// <remarks>
/// For controllers with a controller-level [Route] attribute, the prefix is prepended to the
/// controller route template (e.g. <c>[Route("api/config")]</c> → <c>[Route("apigateway/api/config")]</c>).
/// For controllers without a controller-level route, the prefix is prepended to each action's route template.
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
            if (ctrl.ControllerType.Assembly != typeof(DashboardPagesController).Assembly)
                continue;

            var controllerSelector = ctrl.Selectors
                .FirstOrDefault(s => s.AttributeRouteModel?.Template != null);

            if (controllerSelector != null)
            {
                // Controller has a [Route] attribute – prepend prefix to the controller route
                var template = controllerSelector.AttributeRouteModel?.Template ?? "";
                if (!template.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
                {
                    controllerSelector.AttributeRouteModel!.Template = template.StartsWith("/")
                        ? _prefix + template
                        : _prefix + "/" + template;
                }
                // Action-level routes are left as-is; ASP.NET Core combines them with the controller route
            }
            else
            {
                // No controller-level route – prepend prefix to each action's route
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
}
