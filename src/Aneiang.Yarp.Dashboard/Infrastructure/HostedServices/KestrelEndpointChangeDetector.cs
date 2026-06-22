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
    private readonly DeploymentRestartState _restartState;
    private readonly ILogger<KestrelEndpointChangeDetector> _logger;
    private readonly Dictionary<string, string> _originalEndpoints;
    private readonly Dictionary<string, string?> _originalRestartBoundSettings;
    private string? _lastAlertSignature;
    private Timer? _checkTimer;

    public KestrelEndpointChangeDetector(
        IConfiguration config,
        EndpointRoleResolver resolver,
        IGatewayAlertService alertService,
        DeploymentRestartState restartState,
        ILogger<KestrelEndpointChangeDetector> logger)
    {
        _config = config;
        _resolver = resolver;
        _alertService = alertService;
        _restartState = restartState;
        _logger = logger;
        _originalEndpoints = config.GetSection("Kestrel:Endpoints").GetChildren()
            .Select(e => new { e.Key, Endpoint = NormalizeEndpoint(e["Url"]) })
            .Where(e => e.Endpoint != null)
            .ToDictionary(e => e.Key, e => e.Endpoint!, StringComparer.OrdinalIgnoreCase);
        _originalRestartBoundSettings = CaptureRestartBoundSettings(config);
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
            .Select(e => new { e.Key, Endpoint = NormalizeEndpoint(e["Url"]) })
            .Where(e => e.Endpoint != null)
            .ToDictionary(e => e.Key, e => e.Endpoint!, StringComparer.OrdinalIgnoreCase);

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

        var restartSettingsChanged = DetectRestartBoundSettingChanges();

        if (changed)
        {
            _restartState.MarkRestartRequired(
                "Kestrel:Endpoints",
                "端点配置变更",
                "Kestrel:Endpoints 修改需要重启进程才能生效",
                "Kestrel:Endpoints");
        }

        if (changed || restartSettingsChanged)
        {
            var signature = string.Join(";", currentEndpoints.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => $"{kvp.Key}={kvp.Value}")) + "|" +
                string.Join(";", _restartState.GetReasons().Select(r => r.Key));
            if (string.Equals(_lastAlertSignature, signature, StringComparison.Ordinal)) return;

            _lastAlertSignature = signature;
            _logger.LogWarning("One or more restart-bound deployment settings changed. Restart required for changes to take effect.");
            _alertService.AlertCustom("DeploymentRestartRequired", "部署配置需要重启",
                "端口、TLS、进程模式或 Kestrel 端点等配置修改需要重启进程才能生效", "Warning");
        }
    }

    private bool DetectRestartBoundSettingChanges()
    {
        var current = CaptureRestartBoundSettings(_config);
        var changed = false;
        foreach (var original in _originalRestartBoundSettings)
        {
            current.TryGetValue(original.Key, out var value);
            if (string.Equals(original.Value, value, StringComparison.Ordinal)) continue;

            changed = true;
            _restartState.MarkRestartRequired(
                original.Key,
                "配置需要重启",
                $"{original.Key} 已变更，需要重启进程才能生效",
                original.Key);
        }

        return changed;
    }

    private static Dictionary<string, string?> CaptureRestartBoundSettings(IConfiguration config)
    {
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Gateway:Deployment:Mode"] = config["Gateway:Deployment:Mode"],
            ["Gateway:Deployment:EndpointRoles"] = FlattenSection(config.GetSection("Gateway:Deployment:EndpointRoles")),
            ["Kestrel:Certificates"] = FlattenSection(config.GetSection("Kestrel:Certificates")),
            ["Kestrel:Endpoints"] = FlattenSection(config.GetSection("Kestrel:Endpoints")),
            ["ASPNETCORE_URLS"] = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
        };
    }

    private static string FlattenSection(IConfiguration section)
    {
        return string.Join(";", section.AsEnumerable()
            .Where(kvp => kvp.Value != null)
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => $"{kvp.Key}={kvp.Value}"));
    }

    private static string? NormalizeEndpoint(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url.Trim();

        return $"{uri.Scheme.ToLowerInvariant()}://{NormalizeHost(uri.Host)}:{uri.Port}";
    }

    private static string NormalizeHost(string host) => host switch
    {
        "0.0.0.0" or "*" or "+" or "::" or "[::]" => "*",
        "localhost" => "127.0.0.1",
        _ => host.ToLowerInvariant()
    };

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _checkTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _checkTimer?.Dispose();
        _checkTimer = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _checkTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _checkTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
