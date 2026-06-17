using Aneiang.Yarp.Dashboard.Infrastructure.Alert;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.HostedServices;

/// <summary>
/// Periodically checks whether the <c>Kestrel:Endpoints</c> configuration has changed
/// since startup. Kestrel cannot rebind ports at runtime, so this detector only
/// emits warnings/alerts — actual re-binding requires a process restart.
/// </summary>
public class KestrelEndpointChangeDetector : IHostedService, IDisposable
{
    private readonly IConfiguration _config;
    private readonly EndpointRoleResolver _resolver;
    private readonly IGatewayAlertService _alertService;
    private readonly ILogger<KestrelEndpointChangeDetector> _logger;
    private readonly Dictionary<string, string> _originalEndpoints;
    private Timer? _checkTimer;

    public KestrelEndpointChangeDetector(
        IConfiguration config,
        EndpointRoleResolver resolver,
        IGatewayAlertService alertService,
        ILogger<KestrelEndpointChangeDetector> logger)
    {
        _config = config;
        _resolver = resolver;
        _alertService = alertService;
        _logger = logger;
        _originalEndpoints = resolver.GetAll()
            .ToDictionary(m => m.EndpointName, m => $"{m.IpAddress}:{m.Port}", StringComparer.OrdinalIgnoreCase);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _checkTimer = new Timer(_ => CheckForChanges(), null,
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        return Task.CompletedTask;
    }

    private void CheckForChanges()
    {
        var currentSection = _config.GetSection("Kestrel:Endpoints");
        var currentEndpoints = currentSection.GetChildren()
            .Where(e => !string.IsNullOrEmpty(e["Url"]))
            .ToDictionary(e => e.Key, e => e["Url"] ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        bool changed = false;
        if (currentEndpoints.Count != _originalEndpoints.Count)
        {
            changed = true;
        }
        else
        {
            foreach (var kvp in _originalEndpoints)
            {
                if (!currentEndpoints.TryGetValue(kvp.Key, out var current) || current != kvp.Value)
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            _logger.LogWarning("Kestrel:Endpoints changed in config. Restart required for changes to take effect.");
            _alertService.AlertCustom("KestrelEndpointChange", "端点配置变更",
                "Kestrel:Endpoints 修改需要重启进程才能生效", "Warning");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _checkTimer?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _checkTimer?.Dispose();
}
