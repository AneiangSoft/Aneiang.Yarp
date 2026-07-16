using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure;

internal sealed class AuthOptionsSync : IConfigureOptions<DashboardAuthOptions>
{
    private readonly DashboardOptions _dash;
    public AuthOptionsSync(IOptions<DashboardOptions> dash) => _dash = dash.Value;

    public void Configure(DashboardAuthOptions auth)
    {
        if (_dash.AuthMode != DashboardAuthMode.None && auth.AuthMode == DashboardAuthMode.None)
            auth.AuthMode = _dash.AuthMode;
        auth.ApiKey ??= _dash.ApiKey;
        auth.JwtSecret ??= _dash.JwtSecret;
        auth.JwtUsername ??= _dash.JwtUsername;
        auth.JwtPassword ??= _dash.JwtPassword;
        auth.TwoFactorSecret ??= _dash.TwoFactorSecret;
        auth.AuthorizeRequest ??= _dash.AuthorizeRequest;
    }
}
