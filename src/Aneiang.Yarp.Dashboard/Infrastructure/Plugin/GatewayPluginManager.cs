using System.Text.Json;
using Aneiang.Yarp.Plugins;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>
/// Default implementation of <see cref="IGatewayPluginManager"/>.
/// </summary>
public class GatewayPluginManager : IGatewayPluginManager, IPluginActivationState
{
    private readonly Dictionary<string, IGatewayPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginManifest> _manifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _enabledPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _pluginEnabledAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<GatewayPluginManager> _logger;
    private readonly IGatewaySnapshotPublisher _snapshotPublisher;
    private readonly ExternalGatewayPluginHost _externalPluginHost;
    private readonly IGatewayPluginRepository? _pluginRepository;
    private readonly Dictionary<string, GatewayPluginEntity> _persistedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public GatewayPluginManager(
        IEnumerable<IGatewayPlugin> plugins,
        IConfiguration configuration,
        IHostEnvironment hostEnv,
        ILogger<GatewayPluginManager> logger,
        IGatewaySnapshotPublisher snapshotPublisher,
        ExternalGatewayPluginHost externalPluginHost,
        IGatewayPluginRepository? pluginRepository = null)
    {
        _logger = logger;
        _snapshotPublisher = snapshotPublisher;
        _externalPluginHost = externalPluginHost;
        _pluginRepository = pluginRepository;

        foreach (var plugin in plugins)
        {
            _plugins[plugin.PluginId] = plugin;
            _manifests[plugin.Manifest.Id] = plugin.Manifest;
            _enabledPlugins[plugin.PluginId] = true;
            _pluginEnabledAt[plugin.PluginId] = DateTimeOffset.UtcNow;

            _logger.LogDebug(
                "Plugin '{PluginName}' v{Version} ({PluginId}) registered, enabled: {Enabled}",
                plugin.DisplayName, plugin.Version, plugin.PluginId,
                _enabledPlugins[plugin.PluginId]);
        }

        foreach (var manifest in CreateNativeManifests())
        {
            _manifests[manifest.Id] = manifest;
            _enabledPlugins[manifest.Id] = true;
            _pluginEnabledAt[manifest.Id] = DateTimeOffset.UtcNow;
        }

        foreach (var manifest in _externalPluginHost.Manifests)
        {
            if (_manifests.ContainsKey(manifest.Id))
            {
                _logger.LogWarning("External plugin '{PluginId}' conflicts with an existing plugin and was ignored", manifest.Id);
                continue;
            }

            _manifests[manifest.Id] = manifest;
            _enabledPlugins[manifest.Id] = true;
            _pluginEnabledAt[manifest.Id] = DateTimeOffset.UtcNow;
        }

        // Database is authoritative. The legacy JSON file is imported only when the database has no rows.
        LoadState();

        // External manifests stay metadata-only here. Assemblies are loaded only while preparing
        // an enabled plugin runtime domain, never during manager construction or discovery.
        SaveState();
    }

    /// <inheritdoc />
    public IReadOnlyList<IGatewayPlugin> GetAllPlugins() => _plugins.Values.ToList().AsReadOnly();

    /// <inheritdoc />
    public IGatewayPlugin? GetPlugin(string pluginId) =>
        string.IsNullOrWhiteSpace(pluginId) ? null : _plugins.GetValueOrDefault(pluginId.Trim());

    /// <inheritdoc />
    public IReadOnlyList<PluginManifest> GetAllManifests() =>
        _manifests.Values.OrderBy(manifest => manifest.Order).ThenBy(manifest => manifest.Id, StringComparer.Ordinal).ToArray();

    /// <inheritdoc />
    public PluginManifest? GetManifest(string pluginId) =>
        string.IsNullOrWhiteSpace(pluginId) ? null : _manifests.GetValueOrDefault(pluginId.Trim());

    /// <inheritdoc />
    public bool IsPluginEnabled(string pluginId)
    {
        if (!_enabledPlugins.TryGetValue(pluginId, out var enabled))
            return false;
        return enabled;
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginRuntimeState> GetPluginStates() =>
        GetAllManifests().Select(manifest =>
        {
            var missingDependencies = manifest.Dependencies
                .Where(d => !_enabledPlugins.TryGetValue(d.PluginId, out var enabled) || !enabled)
                .Select(d => d.PluginId).ToArray();
            var bindingTargets = GetBindingTargets(manifest.Id);
            var enabled = IsPluginEnabled(manifest.Id);
            var registration = _externalPluginHost.Registrations.FirstOrDefault(x => string.Equals(x.Manifest.Id, manifest.Id, StringComparison.OrdinalIgnoreCase));
            var health = !enabled ? "Disabled"
                : registration is { Status: ExternalPluginRegistrationStatus.DependencyUnsatisfied or ExternalPluginRegistrationStatus.LoadFailed or ExternalPluginRegistrationStatus.InvalidManifest } ? "Unhealthy"
                : missingDependencies.Length > 0 ? "Degraded" : "Healthy";
            var enabledAt = enabled && _pluginEnabledAt.TryGetValue(manifest.Id, out var at) ? at : (DateTimeOffset?)null;
            return new PluginRuntimeState(
                manifest,
                enabled,
                registration == null,
                missingDependencies,
                bindingTargets,
                health,
                registration?.Status,
                registration?.Error,
                enabledAt);
        }).ToArray();

    public PluginStateChangeResult ValidatePluginStateChange(string pluginId, bool enabled)
    {
        if (!_manifests.ContainsKey(pluginId))
        {
            _logger.LogWarning("Cannot enable/disable unknown plugin: {PluginId}", pluginId);
            return new(false, $"Plugin '{pluginId}' not found.");
        }

        var manifest = _manifests[pluginId];
        if (enabled)
        {
            var missing = manifest.Dependencies
                .Where(d => !_enabledPlugins.TryGetValue(d.PluginId, out var dependencyEnabled) || !dependencyEnabled)
                .Select(d => d.PluginId).ToArray();
            if (missing.Length > 0)
                return new(false, $"Dependencies must be enabled first: {string.Join(", ", missing)}.");
        }
        else
        {
            var dependents = _manifests.Values
                .Where(m => _enabledPlugins.GetValueOrDefault(m.Id) && m.Dependencies.Any(d => string.Equals(d.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)))
                .Select(m => m.Id).ToArray();
            if (dependents.Length > 0)
                return new(false, $"Disable dependents first: {string.Join(", ", dependents)}.");

            var bindingTargets = GetBindingTargets(pluginId);
            if (bindingTargets.Count > 0)
                return new(false, $"Remove plugin bindings first: {string.Join(", ", bindingTargets)}.");
        }

        return PluginStateChangeResult.Success;
    }

    public PluginStateChangeResult SetPluginEnabled(string pluginId, bool enabled)
    {
        var validation = ValidatePluginStateChange(pluginId, enabled);
        if (!validation.Succeeded)
            return validation;

        var previous = _enabledPlugins[pluginId];
        var previousEnabledAt = _pluginEnabledAt.TryGetValue(pluginId, out var previousAt) ? previousAt : (DateTimeOffset?)null;
        _enabledPlugins[pluginId] = enabled;
        if (enabled && !_pluginEnabledAt.ContainsKey(pluginId))
            _pluginEnabledAt[pluginId] = DateTimeOffset.UtcNow;
        else if (!enabled)
            _pluginEnabledAt.Remove(pluginId);
        try
        {
            SaveStateOrThrow();
            _logger.LogInformation("Plugin '{PluginId}' {Action}", pluginId, enabled ? "enabled" : "disabled");
            return PluginStateChangeResult.Success;
        }
        catch (Exception ex)
        {
            _enabledPlugins[pluginId] = previous;
            if (previousEnabledAt.HasValue)
                _pluginEnabledAt[pluginId] = previousEnabledAt.Value;
            else
                _pluginEnabledAt.Remove(pluginId);
            _logger.LogError(ex, "Failed to persist plugin '{PluginId}' state", pluginId);
            return new(false, ex.Message);
        }
    }

    private IReadOnlyList<string> GetBindingTargets(string pluginId)
    {
        var snapshot = _snapshotPublisher.Current;
        var routes = snapshot.RoutePlugins
            .Where(pair => pair.Value.Any(binding => string.Equals(binding.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => $"route:{pair.Key}");
        var clusters = snapshot.ClusterPlugins
            .Where(pair => pair.Value.Any(binding => string.Equals(binding.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => $"cluster:{pair.Key}");
        return routes.Concat(clusters).OrderBy(target => target, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<PluginManifest> CreateNativeManifests() =>
        NativePluginAdapters.Catalog.Select(adapter => new PluginManifest(
            adapter.PluginId,
            adapter.DisplayName,
            "1.0",
            [adapter.Scope == Aneiang.Yarp.Storage.Entities.PluginBindingScope.Route ? PluginScope.Route : PluginScope.Cluster],
            [],
            0,
            new PluginResourceRequirements(),
            [],
            "Built-in adapter that compiles configuration directly to native YARP fields."));

    /// <inheritdoc />
    public void SaveState()
    {
        try
        {
            SaveStateOrThrow();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save plugin state to gateway_plugins");
        }
    }

    private void SaveStateOrThrow()
    {
        if (_pluginRepository == null)
            throw new InvalidOperationException("Plugin state persistence is unavailable because no gateway plugin repository is registered.");

        SaveStateAsync().GetAwaiter().GetResult();
    }

    private async Task SaveStateAsync()
    {
        foreach (var manifest in _manifests.Values)
        {
            var now = DateTime.UtcNow;
            var registration = GetExternalRegistration(manifest.Id);
            _persistedPlugins.TryGetValue(manifest.Id, out var persisted);
            var enabled = _enabledPlugins.GetValueOrDefault(manifest.Id);
            var entity = new GatewayPluginEntity
            {
                PluginId = manifest.Id,
                Version = manifest.Version,
                Enabled = enabled,
                IsBuiltIn = registration == null,
                SourcePath = registration?.ManifestPath,
                RegistrationStatus = registration?.Status.ToString() ?? "Loaded",
                LastError = registration?.Error,
                InstalledAt = persisted?.InstalledAt ?? now,
                UpdatedAt = now,
                EnabledAt = enabled && _pluginEnabledAt.TryGetValue(manifest.Id, out var enabledAt)
                    ? enabledAt.UtcDateTime
                    : (persisted?.EnabledAt)
            };
            await _pluginRepository!.UpsertAsync(entity).ConfigureAwait(false);
            _persistedPlugins[manifest.Id] = entity;
        }

        _logger.LogDebug("Plugin state saved to gateway_plugins");
    }

    private void LoadState()
    {
        if (_pluginRepository == null)
        {
            _logger.LogWarning("Plugin state persistence is unavailable because no gateway plugin repository is registered");
            return;
        }

        try
        {
            var rows = _pluginRepository.GetAllAsync().GetAwaiter().GetResult();

            foreach (var row in rows)
            {
                _persistedPlugins[row.PluginId] = row;
                if (!_manifests.ContainsKey(row.PluginId))
                    continue;

                _enabledPlugins[row.PluginId] = row.Enabled;
                if (row.Enabled && row.EnabledAt.HasValue)
                    _pluginEnabledAt[row.PluginId] = row.EnabledAt.Value;
                else if (!row.Enabled)
                    _pluginEnabledAt.Remove(row.PluginId);
                _logger.LogInformation("Plugin '{PluginId}' state loaded from gateway_plugins: {Enabled}", row.PluginId, row.Enabled);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load plugin state from gateway_plugins");
        }
    }

    /// <inheritdoc />
    public PluginStateChangeResult InstallPlugin(string sourceDirectory)
    {
        if (!_externalPluginHost.TryInstall(sourceDirectory, out var error))
            return new(false, error);

        // Register the newly installed manifest
        var newManifests = _externalPluginHost.Manifests
            .Where(m => !_manifests.ContainsKey(m.Id))
            .ToArray();
        foreach (var manifest in newManifests)
        {
            _manifests[manifest.Id] = manifest;
            _enabledPlugins[manifest.Id] = true;
            _pluginEnabledAt[manifest.Id] = DateTimeOffset.UtcNow;
        }
        SaveState();
        return PluginStateChangeResult.Success;
    }

    /// <inheritdoc />
    public PluginStateChangeResult UninstallPlugin(string pluginId)
    {
        var registration = GetExternalRegistration(pluginId);
        if (registration == null)
            return new(false, $"Plugin '{pluginId}' is a built-in plugin and cannot be uninstalled.");

        if (IsPluginEnabled(pluginId))
            return new(false, "Plugin must be disabled before uninstalling.");

        var bindingTargets = GetBindingTargets(pluginId);
        if (bindingTargets.Count > 0)
            return new(false, $"Remove plugin bindings first: {string.Join(", ", bindingTargets)}.");

        if (!_externalPluginHost.TryUninstall(pluginId, out var error))
            return new(false, error);

        _manifests.Remove(pluginId);
        _enabledPlugins.Remove(pluginId);
        _persistedPlugins.Remove(pluginId);

        // Remove from database
        if (_pluginRepository != null)
        {
            try { _pluginRepository.DeleteAsync(pluginId).GetAwaiter().GetResult(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete plugin {PluginId} from database", pluginId); }
        }

        _logger.LogInformation("Plugin '{PluginId}' uninstalled", pluginId);
        return PluginStateChangeResult.Success;
    }

    /// <inheritdoc />
    public PluginStateChangeResult UpgradePlugin(string pluginId, string sourceDirectory)
    {
        var registration = GetExternalRegistration(pluginId);
        if (registration == null)
            return new(false, $"Plugin '{pluginId}' is a built-in plugin and cannot be upgraded.");

        if (IsPluginEnabled(pluginId))
            return new(false, "Plugin must be disabled before upgrading.");

        if (!_externalPluginHost.TryUpgrade(pluginId, sourceDirectory, out var error))
            return new(false, error);

        // Update manifest in catalog
        var updatedRegistration = GetExternalRegistration(pluginId);
        if (updatedRegistration != null)
        {
            _manifests[pluginId] = updatedRegistration.Manifest;
        }
        SaveState();
        _logger.LogInformation("Plugin '{PluginId}' upgraded", pluginId);
        return PluginStateChangeResult.Success;
    }

    private ExternalPluginRegistration? GetExternalRegistration(string pluginId) =>
        _externalPluginHost.Registrations.FirstOrDefault(x =>
            string.Equals(x.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
}
