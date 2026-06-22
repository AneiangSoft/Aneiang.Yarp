using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Auth;

/// <summary>
/// Configures MVC conventions and filters for the Dashboard:
/// - Sets <see cref="DashboardController.RoutePrefix"/> from options
/// - Resolves JWT secret via <see cref="JwtSecretProvider"/>
/// - Adds <see cref="DashboardRouteConvention"/> for route prefix
/// - Adds <see cref="DashboardAuthFilter"/> when auth is enabled
/// </summary>
internal sealed class DashboardMvcOptionsSetup : IConfigureOptions<MvcOptions>
{
    private readonly IOptions<DashboardOptions> _dashboardOptions;
    private readonly JwtSecretProvider _jwtSecretProvider;

    public DashboardMvcOptionsSetup(
        IOptions<DashboardOptions> dashboardOptions,
        JwtSecretProvider jwtSecretProvider)
    {
        _dashboardOptions = dashboardOptions;
        _jwtSecretProvider = jwtSecretProvider;
    }

    public void Configure(MvcOptions mvcOptions)
    {
        var opts = _dashboardOptions.Value;
        var prefix = opts.RoutePrefix.Trim('/');

        DashboardController.RoutePrefix = prefix;
        opts.JwtSecret = _jwtSecretProvider.GetSecret(opts.JwtSecret);

        mvcOptions.Conventions.Add(new DashboardRouteConvention(prefix));

        if (opts.AuthMode != DashboardAuthMode.None || opts.AuthorizeRequest != null)
        {
            var authFilter = new DashboardAuthFilter(
                new DashboardAuthorizationService(
                    Options.Create(opts),
                    NullLogger<DashboardAuthorizationService>.Instance),
                prefix);
            mvcOptions.Filters.Add(authFilter);
        }
    }
}
