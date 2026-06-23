using Aneiang.Yarp.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Aneiang.Yarp.Services;

internal sealed class GatewayApiAuthConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        var controller = application.Controllers.FirstOrDefault(c => c.ControllerType == typeof(GatewayConfigController));
        if (controller == null) return;

        if (controller.Filters.OfType<ServiceFilterAttribute>().Any(f => f.ServiceType == typeof(GatewayApiAuthFilter)))
            return;

        controller.Filters.Add(new ServiceFilterAttribute(typeof(GatewayApiAuthFilter)) { IsReusable = true });
    }
}
