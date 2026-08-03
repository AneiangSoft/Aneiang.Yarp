using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Plugin management API.
/// </summary>
[Route("api/plugins")]
[ApiController]
public class PluginsController : ControllerBase
{
    private static readonly SemaphoreSlim TransitionGate = new(1, 1);

    private readonly IGatewayPluginManager _manager;
    private readonly IDynamicYarpConfigService _dynamicConfig;
    private readonly IPluginResourceLifecycleCoordinator _resourceLifecycle;
    private readonly IPluginRuntimeDomainManager _runtimeDomains;

    public PluginsController(IGatewayPluginManager manager, IDynamicYarpConfigService dynamicConfig,
        IPluginResourceLifecycleCoordinator resourceLifecycle, IPluginRuntimeDomainManager runtimeDomains)
    {
        _manager = manager;
        _dynamicConfig = dynamicConfig;
        _resourceLifecycle = resourceLifecycle;
        _runtimeDomains = runtimeDomains;
    }

    /// <summary>Get all registered plugins.</summary>
    [HttpGet]
    public async Task<IActionResult> GetPlugins(CancellationToken cancellationToken)
    {
        var states = _manager.GetPluginStates().ToDictionary(x => x.Manifest.Id, StringComparer.OrdinalIgnoreCase);
        var plugins = new List<object>();

        foreach (var manifest in _manager.GetAllManifests())
        {
            var state = states[manifest.Id];
            var actualResources = _resourceLifecycle.GetRuntimeResources(manifest.Id);
            var healthProbe = await CheckHealthAsync(_manager.GetPlugin(manifest.Id), state.Enabled, cancellationToken);
            plugins.Add(new
            {
                pluginId = manifest.Id,
                displayName = manifest.Name,
                version = manifest.Version,
                description = manifest.Description,
                scopes = manifest.Scopes,
                capabilities = manifest.Capabilities,
                order = manifest.Order,
                resources = manifest.Resources,
                declaredResources = GetDeclaredResources(manifest.Resources),
                runtimeResources = actualResources ?? [],
                schemas = manifest.Schemas,
                dependencies = manifest.Dependencies,
                enabled = state.Enabled,
                isBuiltIn = state.IsBuiltIn,
                missingDependencies = state.MissingDependencies,
                bindingTargets = state.BindingTargets,
                bindingCount = state.BindingTargets.Count,
                health = healthProbe?.Status.ToString() ?? state.Health,
                healthProbe,
                registrationStatus = state.RegistrationStatus?.ToString(),
                registrationError = state.RegistrationError,
                unloadSupported = false
            });
        }

        return Ok(new { code = 200, data = plugins });
    }

    private static object[] GetDeclaredResources(PluginResourceRequirements resources)
    {
        var declared = new List<object>();
        if (resources.RequestMiddleware)
            declared.Add(new { type = "RequestMiddleware" });
        if (resources.BackgroundServices)
            declared.Add(new { type = "BackgroundService" });
        if (resources.Database)
            declared.Add(new { type = "Database" });
        if (resources.NetworkConnections)
            declared.Add(new { type = "NetworkConnection" });
        return declared.ToArray();
    }

    private static async Task<PluginHealthProbeResult?> CheckHealthAsync(
        IGatewayPlugin? plugin,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (!enabled)
            return new PluginHealthProbeResult(PluginHealthStatus.Disabled, null, DateTimeOffset.UtcNow);
        if (plugin is not IPluginHealthProbe probe)
            return null;

        try
        {
            return await probe.CheckHealthAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PluginHealthProbeResult(PluginHealthStatus.Unhealthy, ex.Message, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>Get a single plugin by ID.</summary>
    [HttpGet("{pluginId}")]
    public IActionResult GetPlugin(string pluginId)
    {
        var manifest = _manager.GetManifest(pluginId);
        if (manifest == null)
            return NotFound(new { code = 404, message = $"Plugin '{pluginId}' not found" });

        return Ok(new
        {
            code = 200,
            data = new
            {
                pluginId = manifest.Id,
                displayName = manifest.Name,
                version = manifest.Version,
                description = manifest.Description,
                scopes = manifest.Scopes,
                capabilities = manifest.Capabilities,
                order = manifest.Order,
                resources = manifest.Resources,
                schemas = manifest.Schemas,
                enabled = _manager.IsPluginEnabled(pluginId)
            }
        });
    }

    /// <summary>Enable or disable a plugin.</summary>
    [HttpPost("{pluginId}/toggle")]
    public async Task<IActionResult> TogglePlugin(
        string pluginId,
        [FromBody] TogglePluginRequest request,
        CancellationToken cancellationToken)
    {
        var manifest = _manager.GetManifest(pluginId);
        if (manifest == null)
            return NotFound(new { code = 404, message = $"Plugin '{pluginId}' not found" });

        await TransitionGate.WaitAsync(cancellationToken);
        try
        {
            var previousEnabled = _manager.IsPluginEnabled(pluginId);
            if (previousEnabled == request.Enabled)
                return Ok(new { code = 200, data = new { pluginId, enabled = previousEnabled, refreshed = false } });

            var validation = _manager.ValidatePluginStateChange(pluginId, request.Enabled);
            if (!validation.Succeeded)
                return Conflict(new { code = 409, message = validation.Error });

            var targetEnabled = _manager.GetAllManifests()
                .Where(item => string.Equals(item.Id, pluginId, StringComparison.OrdinalIgnoreCase)
                    ? request.Enabled
                    : _manager.IsPluginEnabled(item.Id))
                .Select(item => item.Id)
                .ToArray();
            await using var preparation = await _runtimeDomains.PrepareAsync(targetEnabled, cancellationToken);
            var health = await preparation.CheckHealthAsync(cancellationToken);
            if (health.Status == PluginHealthStatus.Unhealthy)
                return Conflict(new { code = 409, message = health.Message });

            var persisted = _manager.SetPluginEnabled(pluginId, request.Enabled);
            if (!persisted.Succeeded)
                return Conflict(new { code = 409, message = persisted.Error });

            try
            {
                await preparation.CommitAsync(cancellationToken);
                await _resourceLifecycle.ReconcileAsync(cancellationToken);
                _dynamicConfig.RefreshConfig();
            }
            catch
            {
                _manager.SetPluginEnabled(pluginId, previousEnabled);
                await _runtimeDomains.TransitionAsync(GetEnabledPluginIds(), CancellationToken.None);
                await _resourceLifecycle.ReconcileAsync(CancellationToken.None);
                throw;
            }

            return Ok(new { code = 200, data = new { pluginId, enabled = request.Enabled, refreshed = true } });
        }
        finally
        {
            TransitionGate.Release();
        }
    }

    /// <summary>Reset all plugins to enabled state.</summary>
    [HttpPost("reset")]
    public async Task<IActionResult> ResetPlugins(CancellationToken cancellationToken)
    {
        await TransitionGate.WaitAsync(cancellationToken);
        try
        {
            var manifests = _manager.GetAllManifests();
            var previous = manifests.ToDictionary(item => item.Id, item => _manager.IsPluginEnabled(item.Id), StringComparer.OrdinalIgnoreCase);
            var targetEnabled = manifests.Select(item => item.Id).ToArray();
            await using var preparation = await _runtimeDomains.PrepareAsync(targetEnabled, cancellationToken);
            var health = await preparation.CheckHealthAsync(cancellationToken);
            if (health.Status == PluginHealthStatus.Unhealthy)
                return Conflict(new { code = 409, message = health.Message });

            foreach (var manifest in manifests.Where(item => !previous[item.Id]))
            {
                var result = _manager.SetPluginEnabled(manifest.Id, true);
                if (result.Succeeded)
                    continue;

                RestorePluginStates(previous);
                return Conflict(new { code = 409, message = result.Error });
            }

            try
            {
                await preparation.CommitAsync(cancellationToken);
                await _resourceLifecycle.ReconcileAsync(cancellationToken);
                _dynamicConfig.RefreshConfig();
            }
            catch
            {
                RestorePluginStates(previous);
                await _runtimeDomains.TransitionAsync(GetEnabledPluginIds(), CancellationToken.None);
                await _resourceLifecycle.ReconcileAsync(CancellationToken.None);
                throw;
            }

            return Ok(new { code = 200, message = "All plugins enabled" });
        }
        finally
        {
            TransitionGate.Release();
        }
    }

    private string[] GetEnabledPluginIds() => _manager.GetAllManifests()
        .Where(item => _manager.IsPluginEnabled(item.Id))
        .Select(item => item.Id)
        .ToArray();

    private void RestorePluginStates(IReadOnlyDictionary<string, bool> previous)
    {
        foreach (var (pluginId, enabled) in previous)
        {
            if (_manager.IsPluginEnabled(pluginId) != enabled)
                _manager.SetPluginEnabled(pluginId, enabled);
        }
    }
}

public class TogglePluginRequest
{
    public bool Enabled { get; set; }
}
