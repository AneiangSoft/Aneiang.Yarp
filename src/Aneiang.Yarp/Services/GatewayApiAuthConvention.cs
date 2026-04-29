using Aneiang.Yarp.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Aneiang.Yarp.Services;

/// <summary>Applies GatewayApiAuthFilter to GatewayConfigController only.</summary>
internal sealed class GatewayApiAuthConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        var ctrl = application.Controllers.FirstOrDefault(c => c.ControllerType == typeof(GatewayConfigController));
        if (ctrl == null) return;
        ctrl.Filters.Add(new ServiceFilterAttribute(typeof(GatewayApiAuthFilter)) { IsReusable = true });
    }
}
