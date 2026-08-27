using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Microsoft.AspNetCore.Mvc;
using Aneiang.Yarp.Plugins;

using Aneiang.Yarp.Dashboard.Infrastructure.I18n;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>Manages plugin configuration bindings for routes and clusters.</summary>
[ApiController]
[Route("api/plugin-bindings")]
public sealed class PluginBindingsController : ControllerBase
{
    private readonly IPluginConfigurationRepository _repository;
    private readonly IGatewayPluginManager _pluginManager;
    private readonly IDynamicYarpConfigService _dynamicConfig;
    private readonly IPluginConfigurationSchemaValidator _schemaValidator;
    private readonly IPluginConfigurationMigrationService _migrationService;
    private readonly IRouteRepository _routeRepository;
    private readonly IClusterRepository _clusterRepository;
    private readonly IGatewaySnapshotCompiler _snapshotCompiler;
    private static readonly SemaphoreSlim PublishGate = new(1, 1);

    private readonly IGatewaySnapshotPublisher _snapshotPublisher;
    private readonly IPluginRuntimeDomainManager _runtimeDomains;

    public PluginBindingsController(
        IPluginConfigurationRepository repository,
        IGatewayPluginManager pluginManager,
        IDynamicYarpConfigService dynamicConfig,
        IPluginConfigurationSchemaValidator schemaValidator,
        IPluginConfigurationMigrationService migrationService,
        IRouteRepository routeRepository,
        IClusterRepository clusterRepository,
        IGatewaySnapshotCompiler snapshotCompiler,
        IGatewaySnapshotPublisher snapshotPublisher,
        IPluginRuntimeDomainManager runtimeDomains)
    {
        _repository = repository;
        _pluginManager = pluginManager;
        _dynamicConfig = dynamicConfig;
        _schemaValidator = schemaValidator;
        _migrationService = migrationService;
        _routeRepository = routeRepository;
        _clusterRepository = clusterRepository;
        _snapshotCompiler = snapshotCompiler;
        _snapshotPublisher = snapshotPublisher;
        _runtimeDomains = runtimeDomains;
    }

    private string ResolveLocale() =>
        Request.Cookies["dashboard_locale"] == "en-US" ? "en-US" : "zh-CN";

    private string Localize(string i18nKey, string fallback)
    {
        var dict = DashboardI18n.GetDict(ResolveLocale());
        return dict.TryGetValue(i18nKey, out var localized) ? localized : fallback;
    }

    /// <summary>Returns installed plugins and their current enabled state.</summary>
    [HttpGet("plugins")]
    public IActionResult GetInstalledPlugins()
    {
        var plugins = _pluginManager.GetAllManifests()
            .Select(manifest => new InstalledPluginModel(
                manifest.Id,
                Localize($"plugin.name.{manifest.Id}", manifest.Name),
                Localize($"plugin.desc.{manifest.Id}", manifest.Description),
                manifest.Version,
                true,
                _pluginManager.IsPluginEnabled(manifest.Id),
                manifest.Scopes.Select(scope => scope.ToString()).ToArray(),
                manifest.Capabilities.Select(capability => capability.ToString()).ToArray(),
                manifest.Order,
                manifest.Resources,
                manifest.Schemas))
            .OrderBy(plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(new { code = 200, data = plugins });
    }

    /// <summary>Returns bindings, optionally filtered by a route or cluster.</summary>
    [HttpGet]
    public async Task<IActionResult> GetBindings(
        [FromQuery] string? scope,
        [FromQuery] string? scopeId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scope) && string.IsNullOrWhiteSpace(scopeId))
            return Ok(new { code = 200, data = await _repository.GetBindingsAsync(ct) });

        if (!TryParseScope(scope, out var parsedScope) || string.IsNullOrWhiteSpace(scopeId))
            return BadRequest(new { code = 400, message = "Both a valid scope (Route or Cluster) and scopeId are required." });

        return Ok(new { code = 200, data = await _repository.GetBindingsAsync(parsedScope, scopeId.Trim(), ct) });
    }

    /// <summary>Returns one plugin binding.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBinding(string id, CancellationToken ct)
    {
        var binding = await _repository.GetBindingAsync(id, ct);
        return binding == null
            ? NotFound(new { code = 404, message = $"Plugin binding '{id}' was not found." })
            : Ok(new { code = 200, data = binding });
    }

    /// <summary>Creates a plugin binding.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateBinding([FromBody] SavePluginBindingRequest request, CancellationToken ct)
    {
        var validationError = ValidateRequest(request, out var scope, out var normalizedJson);
        if (validationError != null)
            return validationError;

        var target = await ResolveTargetAsync(scope, request.ScopeId.Trim(), ct);
        if (target == null)
            return BadRequest(new { code = 400, message = $"{scope} '{request.ScopeId}' does not exist." });

        var now = DateTime.UtcNow;
        var manifest = _pluginManager.GetManifest(request.PluginId.Trim())!;
        var binding = new PluginBindingEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            PluginId = request.PluginId.Trim(),
            PluginVersion = manifest.Version,
            Scope = scope,
            ScopeId = target.Value.currentId,
            RouteUid = scope == PluginBindingScope.Route ? target.Value.uid : null,
            ClusterUid = scope == PluginBindingScope.Cluster ? target.Value.uid : null,
            Enabled = request.Enabled,
            ConfigJson = normalizedJson,
            SchemaVersion = request.SchemaVersion,
            ConfigVersion = 1,
            Order = request.Order,
            CreatedAt = now,
            UpdatedAt = now
        };

        await PersistAndPublishAsync(binding, ct);
        return CreatedAtAction(nameof(GetBinding), new { id = binding.Id }, new { code = 201, data = binding });
    }

    /// <summary>Updates an existing plugin binding.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBinding(
        string id,
        [FromBody] SavePluginBindingRequest request,
        CancellationToken ct)
    {
        var existing = await _repository.GetBindingAsync(id, ct);
        if (existing == null)
            return NotFound(new { code = 404, message = $"Plugin binding '{id}' was not found." });

        var validationError = ValidateRequest(request, out var scope, out var normalizedJson);
        if (validationError != null)
            return validationError;

        if (!string.Equals(existing.PluginId, request.PluginId.Trim(), StringComparison.OrdinalIgnoreCase) &&
            existing.SchemaVersion != request.SchemaVersion)
            return BadRequest(new { code = 400, message = "PluginId and SchemaVersion cannot be changed in the same update." });

        var candidateJson = normalizedJson;
        if (existing.SchemaVersion != request.SchemaVersion)
        {
            if (!_migrationService.TryMigrate(existing.PluginId, existing.SchemaVersion, request.SchemaVersion,
                    existing.ConfigJson, out candidateJson, out var migrationError))
                return BadRequest(new { code = 400, message = migrationError });
            if (!_schemaValidator.TryValidate(candidateJson, "{\"type\":\"object\"}", out candidateJson, out var migratedJsonError))
                return BadRequest(new { code = 400, message = migratedJsonError });
        }

        var target = await ResolveTargetAsync(scope, request.ScopeId.Trim(), ct);
        if (target == null)
            return BadRequest(new { code = 400, message = $"{scope} '{request.ScopeId}' does not exist." });

        var candidate = CloneBinding(existing);
        candidate.PluginId = request.PluginId.Trim();
        candidate.PluginVersion = _pluginManager.GetManifest(candidate.PluginId)!.Version;
        candidate.Scope = scope;
        candidate.ScopeId = target.Value.currentId;
        candidate.RouteUid = scope == PluginBindingScope.Route ? target.Value.uid : null;
        candidate.ClusterUid = scope == PluginBindingScope.Cluster ? target.Value.uid : null;
        candidate.Enabled = request.Enabled;
        candidate.ConfigJson = candidateJson;
        candidate.SchemaVersion = request.SchemaVersion;
        candidate.ConfigVersion++;
        candidate.Order = request.Order;
        candidate.UpdatedAt = DateTime.UtcNow;

        await PersistAndPublishAsync(candidate, ct);
        return Ok(new { code = 200, data = candidate });
    }

    /// <summary>Deletes a plugin binding.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBinding(string id, CancellationToken ct)
    {
        await PublishGate.WaitAsync(ct);
        try
        {
            var existing = await _repository.GetBindingAsync(id, ct);
            if (existing == null)
                return NotFound(new { code = 404, message = $"Plugin binding '{id}' was not found." });

            var bindings = (await _repository.GetBindingsAsync(ct)).Where(x => x.Id != id).ToArray();
            var (ok, error) = await CompilePrepareAndApplyAsync(
                bindings,
                applyAsync: async () =>
                {
                    if (!await _repository.DeleteBindingAsync(id, ct))
                        return false;
                    return true;
                },
                rollbackAsync: async () => await _repository.UpsertBindingAsync(existing, CancellationToken.None),
                ct);
            if (!ok)
                return StatusCode(500, new { code = 500, message = error });

            _dynamicConfig.RefreshConfig();
            return Ok(new { code = 200, message = "Plugin binding deleted." });
        }
        finally
        {
            PublishGate.Release();
        }
    }

    /// <summary>
    /// Batch-create plugin bindings for multiple routes/clusters in one atomic cycle
    /// (one snapshot compile + one runtime domain preparation + one health check + one publish).
    /// Existing bindings for the same (pluginId, scope, scopeId) are skipped by default;
    /// set <c>overwriteExisting</c> to overwrite their ConfigJson/Enabled/Order.
    /// </summary>
    [HttpPost("batch-create")]
    public async Task<IActionResult> BatchCreateBindings([FromBody] BatchCreateBindingsRequest request, CancellationToken ct)
    {
        if (request is null || request.ScopeIds is null || request.ScopeIds.Count == 0)
            return BadRequest(new { code = 400, message = "scopeIds is required and must be non-empty" });

        if (!TryParseScope(request.Scope, out var scope))
            return BadRequest(new { code = 400, message = "Scope must be Route or Cluster." });

        var pluginId = (request.PluginId ?? string.Empty).Trim();
        var manifest = _pluginManager.GetManifest(pluginId);
        if (string.IsNullOrWhiteSpace(pluginId) || manifest == null)
            return BadRequest(new { code = 400, message = $"Plugin '{request.PluginId}' is not installed." });

        if (request.Enabled && !_pluginManager.IsPluginEnabled(pluginId))
            return BadRequest(new { code = 400, message = $"Plugin '{pluginId}' is disabled and cannot have an enabled binding." });

        if (!GetSupportedScopes(pluginId).Contains(scope))
            return BadRequest(new { code = 400, message = $"Plugin '{pluginId}' does not support {scope} bindings." });

        if (request.SchemaVersion < 1)
            return BadRequest(new { code = 400, message = "SchemaVersion must be greater than zero." });

        // Validate schema once for the shared config payload.
        var schema = manifest.Schemas.SingleOrDefault(s => s.Version == request.SchemaVersion);
        if (schema == null)
            return BadRequest(new { code = 400, message = $"Plugin '{pluginId}' does not declare configuration schema v{request.SchemaVersion}." });
        string normalizedJson;
        if (!_schemaValidator.TryValidate(request.ConfigJson ?? string.Empty, schema.ConfigJsonSchema, out normalizedJson, out var schemaError))
            return BadRequest(new { code = 400, message = $"ConfigJson is invalid for '{pluginId}' schema v{request.SchemaVersion}: {schemaError}" });

        await PublishGate.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            var existing = await _repository.GetBindingsAsync(ct);
            var items = new List<BatchItemResult>(request.ScopeIds.Count);
            var toUpsert = new List<PluginBindingEntity>();
            var previousForRollback = new List<PluginBindingEntity?>();

            foreach (var rawScopeId in request.ScopeIds)
            {
                var scopeId = (rawScopeId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(scopeId))
                {
                    items.Add(new BatchItemResult(scopeId, false, $"{scope} target UID is required."));
                    continue;
                }

                var target = await ResolveTargetAsync(scope, scopeId, ct);
                if (target == null)
                {
                    items.Add(new BatchItemResult(scopeId, false, $"{scope} '{scopeId}' does not exist."));
                    continue;
                }

                var match = existing.FirstOrDefault(b =>
                    string.Equals(b.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)
                    && b.Scope == scope
                    && string.Equals(b.ScopeId, target.Value.currentId, StringComparison.OrdinalIgnoreCase));

                if (match != null && !request.OverwriteExisting)
                {
                    items.Add(new BatchItemResult(scopeId, true, $"Binding already exists for '{pluginId}' on {scope} '{scopeId}' — skipped."));
                    continue;
                }

                PluginBindingEntity candidate;
                if (match != null)
                {
                    previousForRollback.Add(CloneBinding(match));
                    candidate = CloneBinding(match);
                    candidate.PluginVersion = manifest.Version;
                    candidate.Scope = scope;
                    candidate.ScopeId = target.Value.currentId;
                    candidate.RouteUid = scope == PluginBindingScope.Route ? target.Value.uid : null;
                    candidate.ClusterUid = scope == PluginBindingScope.Cluster ? target.Value.uid : null;
                    candidate.Enabled = request.Enabled;
                    candidate.ConfigJson = normalizedJson;
                    candidate.SchemaVersion = request.SchemaVersion;
                    candidate.ConfigVersion = match.ConfigVersion + 1;
                    candidate.Order = request.Order;
                    candidate.UpdatedAt = now;
                }
                else
                {
                    previousForRollback.Add(null);
                    candidate = new PluginBindingEntity
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        PluginId = pluginId,
                        PluginVersion = manifest.Version,
                        Scope = scope,
                        ScopeId = target.Value.currentId,
                        RouteUid = scope == PluginBindingScope.Route ? target.Value.uid : null,
                        ClusterUid = scope == PluginBindingScope.Cluster ? target.Value.uid : null,
                        Enabled = request.Enabled,
                        ConfigJson = normalizedJson,
                        SchemaVersion = request.SchemaVersion,
                        ConfigVersion = 1,
                        Order = request.Order,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                }

                toUpsert.Add(candidate);
                items.Add(new BatchItemResult(scopeId, true,
                    match != null ? $"Binding updated for '{pluginId}' on {scope} '{scopeId}'." : $"Binding created for '{pluginId}' on {scope} '{scopeId}'."));
            }

            if (toUpsert.Count == 0)
                return Ok(BuildBatchResponse(items, "No bindings to create (all skipped)."));

            var mergedBindings = existing
                .Where(b => !toUpsert.Any(c => c.Id == b.Id))
                .Concat(toUpsert)
                .ToArray();

            var (ok, error) = await CompilePrepareAndApplyAsync(
                mergedBindings,
                applyAsync: async () =>
                {
                    foreach (var candidate in toUpsert)
                        await _repository.UpsertBindingAsync(candidate, ct);
                    return true;
                },
                rollbackAsync: async () =>
                {
                    for (var i = 0; i < toUpsert.Count; i++)
                    {
                        var prev = previousForRollback[i];
                        if (prev == null)
                            await _repository.DeleteBindingAsync(toUpsert[i].Id, CancellationToken.None);
                        else
                            await _repository.UpsertBindingAsync(prev, CancellationToken.None);
                    }
                },
                ct);
            if (!ok)
                return StatusCode(500, new { code = 500, message = error });

            _dynamicConfig.RefreshConfig();
            return Ok(BuildBatchResponse(items, "Batch create plugin bindings."));
        }
        finally
        {
            PublishGate.Release();
        }
    }

    /// <summary>
    /// Batch-enable or batch-disable existing plugin bindings in one atomic cycle.
    /// </summary>
    [HttpPost("batch-enabled")]
    public async Task<IActionResult> BatchSetBindingsEnabled([FromBody] BatchSetBindingsEnabledRequest request, CancellationToken ct)
    {
        if (request is null || request.BindingIds is null || request.BindingIds.Count == 0)
            return BadRequest(new { code = 400, message = "bindingIds is required and must be non-empty" });

        await PublishGate.WaitAsync(ct);
        try
        {
            var existing = await _repository.GetBindingsAsync(ct);
            var items = new List<BatchItemResult>(request.BindingIds.Count);
            var toUpsert = new List<PluginBindingEntity>();
            var previousForRollback = new List<PluginBindingEntity>();

            foreach (var rawId in request.BindingIds)
            {
                var id = (rawId ?? string.Empty).Trim();
                var match = existing.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.Ordinal));
                if (match == null)
                {
                    items.Add(new BatchItemResult(id, false, $"Binding '{id}' not found."));
                    continue;
                }

                if (match.Enabled == request.Enabled)
                {
                    items.Add(new BatchItemResult(id, true, $"Binding '{id}' already {(request.Enabled ? "enabled" : "disabled")}."));
                    continue;
                }

                previousForRollback.Add(CloneBinding(match));
                var candidate = CloneBinding(match);
                candidate.Enabled = request.Enabled;
                candidate.ConfigVersion = match.ConfigVersion + 1;
                candidate.UpdatedAt = DateTime.UtcNow;
                toUpsert.Add(candidate);
                items.Add(new BatchItemResult(id, true, $"Binding '{id}' {(request.Enabled ? "enabled" : "disabled")}."));
            }

            if (toUpsert.Count == 0)
                return Ok(BuildBatchResponse(items, "No bindings to update."));

            var mergedBindings = existing
                .Where(b => !toUpsert.Any(c => c.Id == b.Id))
                .Concat(toUpsert)
                .ToArray();

            var (ok, error) = await CompilePrepareAndApplyAsync(
                mergedBindings,
                applyAsync: async () =>
                {
                    foreach (var candidate in toUpsert)
                        await _repository.UpsertBindingAsync(candidate, ct);
                    return true;
                },
                rollbackAsync: async () =>
                {
                    foreach (var prev in previousForRollback)
                        await _repository.UpsertBindingAsync(prev, CancellationToken.None);
                },
                ct);
            if (!ok)
                return StatusCode(500, new { code = 500, message = error });

            _dynamicConfig.RefreshConfig();
            return Ok(BuildBatchResponse(items, "Batch set plugin bindings enabled."));
        }
        finally
        {
            PublishGate.Release();
        }
    }

    /// <summary>
    /// Batch-delete plugin bindings in one atomic cycle.
    /// </summary>
    [HttpPost("batch-delete")]
    public async Task<IActionResult> BatchDeleteBindings([FromBody] BatchDeleteBindingsRequest request, CancellationToken ct)
    {
        if (request is null || request.BindingIds is null || request.BindingIds.Count == 0)
            return BadRequest(new { code = 400, message = "bindingIds is required and must be non-empty" });

        await PublishGate.WaitAsync(ct);
        try
        {
            var existing = await _repository.GetBindingsAsync(ct);
            var targetIds = new HashSet<string>(request.BindingIds
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i!.Trim()), StringComparer.Ordinal);

            var items = new List<BatchItemResult>(request.BindingIds.Count);
            var deletedBindings = new List<PluginBindingEntity>();
            foreach (var id in request.BindingIds.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i!.Trim()))
            {
                var match = existing.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.Ordinal));
                if (match == null)
                {
                    items.Add(new BatchItemResult(id, false, $"Binding '{id}' not found."));
                    continue;
                }
                deletedBindings.Add(match);
                items.Add(new BatchItemResult(id, true, $"Binding '{id}' deleted."));
            }

            if (deletedBindings.Count == 0)
                return Ok(BuildBatchResponse(items, "No bindings to delete."));

            var mergedBindings = existing.Where(b => !targetIds.Contains(b.Id)).ToArray();

            var (ok, error) = await CompilePrepareAndApplyAsync(
                mergedBindings,
                applyAsync: async () =>
                {
                    foreach (var b in deletedBindings)
                        await _repository.DeleteBindingAsync(b.Id, ct);
                    return true;
                },
                rollbackAsync: async () =>
                {
                    foreach (var b in deletedBindings)
                        await _repository.UpsertBindingAsync(b, CancellationToken.None);
                },
                ct);
            if (!ok)
                return StatusCode(500, new { code = 500, message = error });

            _dynamicConfig.RefreshConfig();
            return Ok(BuildBatchResponse(items, "Batch delete plugin bindings."));
        }
        finally
        {
            PublishGate.Release();
        }
    }

    /// <summary>
    /// Shared atomic flow: compile snapshot once, prepare runtime domain once,
    /// health check once; on success run <paramref name="applyAsync"/> (repo mutations)
    /// then commit runtime + publish snapshot; on failure run <paramref name="rollbackAsync"/>.
    /// Caller must hold <see cref="PublishGate"/>.
    /// </summary>
    private async Task<(bool ok, string? error)> CompilePrepareAndApplyAsync(
        IReadOnlyList<PluginBindingEntity> candidateBindings,
        Func<Task<bool>> applyAsync,
        Func<Task> rollbackAsync,
        CancellationToken ct)
    {
        var current = _snapshotPublisher.Current;
        var snapshot = await _snapshotCompiler.CompileAsync(
            _dynamicConfig.GetRoutes(), _dynamicConfig.GetClusters(), current.Version + 1, ct, candidateBindings);
        var enabledPluginIds = GetEnabledPluginIds(candidateBindings);
        await using var runtimePreparation = await _runtimeDomains.PrepareAsync(enabledPluginIds, ct);
        var health = await runtimePreparation.CheckHealthAsync(ct);
        if (health.Status == PluginHealthStatus.Unhealthy)
            return (false, health.Message ?? "Candidate plugin runtime domain is unhealthy.");

        var applied = false;
        try
        {
            applied = await applyAsync();
            if (!applied)
                return (false, "Repository mutation reported failure.");
            await runtimePreparation.CommitAsync(ct);
            _snapshotPublisher.Publish(snapshot);
            return (true, null);
        }
        catch
        {
            if (applied)
            {
                try { await rollbackAsync(); } catch { /* best-effort rollback; original error will surface */ }
            }
            throw;
        }
    }

    private static object BuildBatchResponse(List<BatchItemResult> items, string summary)
    {
        var succeeded = items.Count(x => x.Success);
        var failed = items.Count - succeeded;
        return new
        {
            code = 200,
            message = $"{summary} ({succeeded} succeeded, {failed} failed)",
            data = new
            {
                succeeded,
                failed,
                total = items.Count,
                items
            }
        };
    }

    private IActionResult? ValidateRequest(
        SavePluginBindingRequest request,
        out PluginBindingScope scope,
        out string normalizedJson)
    {
        scope = default;
        normalizedJson = string.Empty;

        var pluginId = request.PluginId?.Trim() ?? string.Empty;
        var manifest = _pluginManager.GetManifest(pluginId);
        if (string.IsNullOrWhiteSpace(pluginId) || manifest == null)
            return BadRequest(new { code = 400, message = $"Plugin '{request.PluginId}' is not installed." });

        if (request.Enabled && !_pluginManager.IsPluginEnabled(pluginId))
            return BadRequest(new { code = 400, message = $"Plugin '{request.PluginId}' is disabled and cannot have an enabled binding." });

        if (!TryParseScope(request.Scope, out scope))
            return BadRequest(new { code = 400, message = "Scope must be Route or Cluster." });

        if (!GetSupportedScopes(pluginId).Contains(scope))
            return BadRequest(new { code = 400, message = $"Plugin '{pluginId}' does not support {scope} bindings." });

        if (string.IsNullOrWhiteSpace(request.ScopeId))
            return BadRequest(new { code = 400, message = $"{scope} target UID is required." });

        if (request.SchemaVersion < 1)
            return BadRequest(new { code = 400, message = "SchemaVersion must be greater than zero." });

        var schema = manifest.Schemas.SingleOrDefault(candidate => candidate.Version == request.SchemaVersion);
        if (schema == null)
            return BadRequest(new { code = 400, message = $"Plugin '{pluginId}' does not declare configuration schema v{request.SchemaVersion}." });
        if (!_schemaValidator.TryValidate(request.ConfigJson ?? string.Empty, schema.ConfigJsonSchema, out normalizedJson, out var schemaError))
            return BadRequest(new { code = 400, message = $"ConfigJson is invalid for '{pluginId}' schema v{request.SchemaVersion}: {schemaError}" });

        return null;
    }

    private async Task<(string uid, string currentId)?> ResolveTargetAsync(
        PluginBindingScope scope, string targetUid, CancellationToken ct)
    {
        if (scope == PluginBindingScope.Route)
        {
            var routes = await _routeRepository.GetAllRoutesAsync(ct);
            var route = routes.FirstOrDefault(x => string.Equals(x.RouteUid, targetUid, StringComparison.Ordinal));
            if (route != null) return (route.RouteUid, route.RouteId);
            route = routes.FirstOrDefault(x => string.Equals(x.RouteId, targetUid, StringComparison.OrdinalIgnoreCase));
            return route == null ? null : (route.RouteUid, route.RouteId);
        }

        var clusters = await _clusterRepository.GetAllClustersAsync(ct);
        var cluster = clusters.FirstOrDefault(x => string.Equals(x.ClusterUid, targetUid, StringComparison.Ordinal));
        if (cluster != null) return (cluster.ClusterUid, cluster.ClusterId);
        cluster = clusters.FirstOrDefault(x => string.Equals(x.ClusterId, targetUid, StringComparison.OrdinalIgnoreCase));
        return cluster == null ? null : (cluster.ClusterUid, cluster.ClusterId);
    }

    private async Task PersistAndPublishAsync(PluginBindingEntity candidate, CancellationToken ct)
    {
        await PublishGate.WaitAsync(ct);
        try
        {
            var previous = await _repository.GetBindingAsync(candidate.Id, ct);
            var bindings = (await _repository.GetBindingsAsync(ct)).Where(x => x.Id != candidate.Id).Append(candidate).ToArray();
            var current = _snapshotPublisher.Current;
            var routes = _dynamicConfig.GetRoutes();
            var clusters = _dynamicConfig.GetClusters();
            var snapshot = await _snapshotCompiler.CompileAsync(routes, clusters, current.Version + 1, ct, bindings);
            var enabledPluginIds = GetEnabledPluginIds(bindings);
            await using var runtimePreparation = await _runtimeDomains.PrepareAsync(enabledPluginIds, ct);
            var health = await runtimePreparation.CheckHealthAsync(ct);
            if (health.Status == PluginHealthStatus.Unhealthy)
                throw new InvalidOperationException(health.Message ?? "Candidate plugin runtime domain is unhealthy.");

            await _repository.UpsertBindingAsync(candidate, ct);
            try
            {
                await runtimePreparation.CommitAsync(ct);
                _snapshotPublisher.Publish(snapshot);
            }
            catch
            {
                if (previous == null)
                    await _repository.DeleteBindingAsync(candidate.Id, CancellationToken.None);
                else
                    await _repository.UpsertBindingAsync(previous, CancellationToken.None);
                throw;
            }

            _dynamicConfig.RefreshConfig();
        }
        finally
        {
            PublishGate.Release();
        }
    }

    private string[] GetEnabledPluginIds(IReadOnlyCollection<PluginBindingEntity> bindings)
    {
        var knownIds = _pluginManager.GetAllManifests().Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return bindings
            .Where(x => x.Enabled && knownIds.Contains(x.PluginId))
            .Select(x => x.PluginId)
            .Concat(_pluginManager.GetAllManifests().Where(x => _pluginManager.IsPluginEnabled(x.Id)).Select(x => x.Id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PluginBindingEntity CloneBinding(PluginBindingEntity source) => new()
    {
        Id = source.Id,
        PluginId = source.PluginId,
        PluginVersion = source.PluginVersion,
        Scope = source.Scope,
        ScopeId = source.ScopeId,
        RouteUid = source.RouteUid,
        ClusterUid = source.ClusterUid,
        Enabled = source.Enabled,
        ConfigJson = source.ConfigJson,
        SchemaVersion = source.SchemaVersion,
        ConfigVersion = source.ConfigVersion,
        Order = source.Order,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt
    };

    private IReadOnlyList<PluginBindingScope> GetSupportedScopes(string pluginId) =>
        _pluginManager.GetManifest(pluginId)?.Scopes.Select(ToStorageScope).ToArray()
        ?? Array.Empty<PluginBindingScope>();

    private static PluginBindingScope ToStorageScope(PluginScope scope) => scope switch
    {
        PluginScope.Route => PluginBindingScope.Route,
        PluginScope.Cluster => PluginBindingScope.Cluster,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
    };

    private static bool TryParseScope(string? value, out PluginBindingScope scope)
    {
        return Enum.TryParse(value, true, out scope) && Enum.IsDefined(scope);
    }

}

public sealed class SavePluginBindingRequest
{
    public string PluginId { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string ScopeId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string ConfigJson { get; set; } = "{}";
    public int SchemaVersion { get; set; } = 1;
    public int Order { get; set; }
}

/// <summary>Request body for batch-creating plugin bindings across multiple routes/clusters.</summary>
public sealed class BatchCreateBindingsRequest
{
    /// <summary>Plugin id to bind (e.g. "CircuitBreaker").</summary>
    public string PluginId { get; set; } = string.Empty;
    /// <summary>"Route" or "Cluster".</summary>
    public string Scope { get; set; } = string.Empty;
    /// <summary>Route/cluster UIDs (or current ids) to bind the plugin to.</summary>
    public List<string> ScopeIds { get; set; } = new();
    /// <summary>Shared configuration JSON for all bindings in this batch.</summary>
    public string ConfigJson { get; set; } = "{}";
    /// <summary>Whether the new bindings should be enabled.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Plugin configuration schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Execution order for the bindings.</summary>
    public int Order { get; set; }
    /// <summary>
    /// When true, existing bindings for the same (pluginId, scope, scopeId) are overwritten
    /// with the new ConfigJson/Enabled/Order. Default false: existing bindings are skipped.
    /// </summary>
    public bool OverwriteExisting { get; set; }
}

/// <summary>Request body for batch enabling/disabling plugin bindings.</summary>
public sealed class BatchSetBindingsEnabledRequest
{
    public List<string> BindingIds { get; set; } = new();
    public bool Enabled { get; set; }
}

/// <summary>Request body for batch deleting plugin bindings.</summary>
public sealed class BatchDeleteBindingsRequest
{
    public List<string> BindingIds { get; set; } = new();
}

public sealed record InstalledPluginModel(
    string PluginId,
    string DisplayName,
    string Description,
    string Version,
    bool Installed,
    bool Enabled,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Capabilities,
    int Order,
    PluginResourceRequirements Resources,
    IReadOnlyList<PluginSchemaReference> Schemas);
