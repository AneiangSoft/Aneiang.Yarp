using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

public interface IPluginBindingMutationService
{
    Task UpsertAsync(PluginBindingEntity candidate, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string bindingId, CancellationToken cancellationToken = default);
}

public sealed class PluginBindingMutationService : IPluginBindingMutationService
{
    private static readonly SemaphoreSlim PublishGate = new(1, 1);

    private readonly IPluginConfigurationRepository _repository;
    private readonly IGatewayPluginManager _pluginManager;
    private readonly IDynamicYarpConfigService _dynamicConfig;
    private readonly IGatewaySnapshotCompiler _snapshotCompiler;
    private readonly IGatewaySnapshotPublisher _snapshotPublisher;
    private readonly IPluginRuntimeDomainManager _runtimeDomains;

    public PluginBindingMutationService(
        IPluginConfigurationRepository repository,
        IGatewayPluginManager pluginManager,
        IDynamicYarpConfigService dynamicConfig,
        IGatewaySnapshotCompiler snapshotCompiler,
        IGatewaySnapshotPublisher snapshotPublisher,
        IPluginRuntimeDomainManager runtimeDomains)
    {
        _repository = repository;
        _pluginManager = pluginManager;
        _dynamicConfig = dynamicConfig;
        _snapshotCompiler = snapshotCompiler;
        _snapshotPublisher = snapshotPublisher;
        _runtimeDomains = runtimeDomains;
    }

    public async Task UpsertAsync(PluginBindingEntity candidate, CancellationToken cancellationToken = default)
    {
        await PublishGate.WaitAsync(cancellationToken);
        try
        {
            var previous = await _repository.GetBindingAsync(candidate.Id, cancellationToken);
            var bindings = (await _repository.GetBindingsAsync(cancellationToken))
                .Where(binding => binding.Id != candidate.Id)
                .Append(candidate)
                .ToArray();
            var snapshot = await CompileCandidateAsync(bindings, cancellationToken);
            await using var preparation = await PrepareRuntimeAsync(bindings, cancellationToken);

            await _repository.UpsertBindingAsync(candidate, cancellationToken);
            try
            {
                await preparation.CommitAsync(cancellationToken);
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

    public async Task<bool> DeleteAsync(string bindingId, CancellationToken cancellationToken = default)
    {
        await PublishGate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _repository.GetBindingAsync(bindingId, cancellationToken);
            if (existing == null)
                return false;

            var bindings = (await _repository.GetBindingsAsync(cancellationToken))
                .Where(binding => binding.Id != bindingId)
                .ToArray();
            var snapshot = await CompileCandidateAsync(bindings, cancellationToken);
            await using var preparation = await PrepareRuntimeAsync(bindings, cancellationToken);

            if (!await _repository.DeleteBindingAsync(bindingId, cancellationToken))
                return false;

            try
            {
                await preparation.CommitAsync(cancellationToken);
                _snapshotPublisher.Publish(snapshot);
            }
            catch
            {
                await _repository.UpsertBindingAsync(existing, CancellationToken.None);
                throw;
            }

            _dynamicConfig.RefreshConfig();
            return true;
        }
        finally
        {
            PublishGate.Release();
        }
    }

    private Task<GatewaySnapshot> CompileCandidateAsync(
        IReadOnlyList<PluginBindingEntity> bindings,
        CancellationToken cancellationToken) =>
        _snapshotCompiler.CompileAsync(
            _dynamicConfig.GetRoutes(),
            _dynamicConfig.GetClusters(),
            _snapshotPublisher.Current.Version + 1,
            cancellationToken,
            bindings);

    private async Task<PluginRuntimeDomainPreparation> PrepareRuntimeAsync(
        IReadOnlyCollection<PluginBindingEntity> bindings,
        CancellationToken cancellationToken)
    {
        var enabledPluginIds = bindings.Where(binding => binding.Enabled).Select(binding => binding.PluginId)
            .Concat(_pluginManager.GetAllManifests()
                .Where(manifest => _pluginManager.IsPluginEnabled(manifest.Id))
                .Select(manifest => manifest.Id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var preparation = await _runtimeDomains.PrepareAsync(enabledPluginIds, cancellationToken);
        var health = await preparation.CheckHealthAsync(cancellationToken);
        if (health.Status == PluginHealthStatus.Unhealthy)
        {
            await preparation.DisposeAsync();
            throw new InvalidOperationException(health.Message ?? "Candidate plugin runtime domain is unhealthy.");
        }

        return preparation;
    }
}
