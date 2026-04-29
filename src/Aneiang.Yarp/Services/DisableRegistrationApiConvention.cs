using Aneiang.Yarp.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Removes GatewayConfigController from the MVC application model
/// when dynamic route registration API is disabled.
/// </summary>
internal sealed class DisableRegistrationApiConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        var toRemove = application.Controllers
            .Where(c => c.ControllerType == typeof(GatewayConfigController))
            .ToList();

        foreach (var controller in toRemove)
            application.Controllers.Remove(controller);
    }
}
