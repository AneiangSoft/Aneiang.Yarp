using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure;

internal sealed class ProxyLogOptionsSync : IConfigureOptions<ProxyLogOptions>
{
    private readonly DashboardOptions _dash;
    public ProxyLogOptionsSync(IOptions<DashboardOptions> dash) => _dash = dash.Value;

    public void Configure(ProxyLogOptions proxyLog)
    {
        if (_dash.EnableProxyLogging && !proxyLog.EnableProxyLogging)
            proxyLog.EnableProxyLogging = true;
    }
}
