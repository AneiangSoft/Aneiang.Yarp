using Aneiang.Yarp.Dashboard.Infrastructure.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Auth;

/// <summary>
/// Configures MVC conventions and filters for the Dashboard:
/// - Sets <see cref="IDashboardRouteAccessor.RoutePrefix"/> from options
/// - Resolves JWT secret via <see cref="JwtSecretProvider"/>
/// - Adds <see cref="DashboardRouteConvention"/> for route prefix
/// - Adds <see cref="DashboardAuthFilter"/> when auth is enabled
/// - Adds <see cref="GlobalExceptionFilter"/> for unified error handling
/// - Adds <see cref="FluentValidationFilter"/> for automatic DTO validation
/// </summary>
internal sealed class DashboardMvcOptionsSetup : IConfigureOptions<MvcOptions>
{
    private readonly IOptions<DashboardOptions> _dashboardOptions;
    private readonly JwtSecretProvider _jwtSecretProvider;
    private readonly IDashboardAuthorizationService _authService;
    private readonly GlobalExceptionFilter _globalExceptionFilter;
    private readonly FluentValidationFilter _validationFilter;
    private readonly DashboardRouteAccessor _routeAccessor;

    public DashboardMvcOptionsSetup(
        IOptions<DashboardOptions> dashboardOptions,
        JwtSecretProvider jwtSecretProvider,
        IDashboardAuthorizationService authService,
        GlobalExceptionFilter globalExceptionFilter,
        FluentValidationFilter validationFilter,
        DashboardRouteAccessor routeAccessor)
    {
        _dashboardOptions = dashboardOptions;
        _jwtSecretProvider = jwtSecretProvider;
        _authService = authService;
        _globalExceptionFilter = globalExceptionFilter;
        _validationFilter = validationFilter;
        _routeAccessor = routeAccessor;
    }

    public void Configure(MvcOptions mvcOptions)
    {
        var opts = _dashboardOptions.Value;
        var prefix = opts.RoutePrefix.Trim('/');

        _routeAccessor.RoutePrefix = prefix;
        opts.JwtSecret = _jwtSecretProvider.GetSecret(opts.JwtSecret);

        mvcOptions.Conventions.Add(new DashboardRouteConvention(prefix));
        mvcOptions.Filters.Add(_globalExceptionFilter);
        mvcOptions.Filters.Add(_validationFilter);

        if (opts.AuthMode != DashboardAuthMode.None || opts.AuthorizeRequest != null)
        {
            var authFilter = new DashboardAuthFilter(_authService, prefix);
            mvcOptions.Filters.Add(authFilter);
        }
    }
}
