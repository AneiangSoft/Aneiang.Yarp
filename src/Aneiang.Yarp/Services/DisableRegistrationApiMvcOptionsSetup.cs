using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Configures MVC conventions to remove the GatewayConfigController
/// from the application model when dynamic route registration API is disabled.
/// </summary>
internal sealed class DisableRegistrationApiMvcOptionsSetup : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options)
    {
        options.Conventions.Add(new DisableRegistrationApiConvention());
    }
}
