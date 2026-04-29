using Aneiang.Yarp.Dashboard.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>Prepends the dashboard route prefix to all DashboardController actions.</summary>
internal sealed class DashboardRouteConvention : IApplicationModelConvention
{
    private readonly string _prefix;
    public DashboardRouteConvention(string prefix) => _prefix = prefix;

    public void Apply(ApplicationModel application)
    {
        var ctrl = application.Controllers.FirstOrDefault(c => c.ControllerType == typeof(DashboardController));
        if (ctrl == null) return;

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
