using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Configures MVC conventions for gateway API authentication.
/// Registers <see cref="GatewayApiAuthConvention"/> on the GatewayConfigController.
/// </summary>
public sealed class GatewayApiAuthMvcOptionsSetup : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options)
    {
        options.Conventions.Add(new GatewayApiAuthConvention());
    }
}
