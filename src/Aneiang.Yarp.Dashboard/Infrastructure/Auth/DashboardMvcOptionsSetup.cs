using Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;
using Microsoft.AspNetCore.Mvc;
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
    private readonly IDashboardAuthorizationService _authService;

    public DashboardMvcOptionsSetup(
        IOptions<DashboardOptions> dashboardOptions,
        JwtSecretProvider jwtSecretProvider,
        IDashboardAuthorizationService authService)
    {
        _dashboardOptions = dashboardOptions;
        _jwtSecretProvider = jwtSecretProvider;
        _authService = authService;
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
            var authFilter = new DashboardAuthFilter(_authService, prefix);
            mvcOptions.Filters.Add(authFilter);
        }
    }
}
