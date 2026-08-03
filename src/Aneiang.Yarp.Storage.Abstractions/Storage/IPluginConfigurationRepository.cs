using Aneiang.Yarp.Storage.Entities;

namespace Aneiang.Yarp.Storage;

/// <summary>Stores plugin schemas and route/cluster configuration bindings.</summary>
public interface IPluginConfigurationRepository
{
    Task<IReadOnlyList<PluginBindingEntity>> GetBindingsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PluginBindingEntity>> GetBindingsAsync(PluginBindingScope scope, string scopeId, CancellationToken ct = default);
    Task<PluginBindingEntity?> GetBindingAsync(string id, CancellationToken ct = default);
    Task UpsertBindingAsync(PluginBindingEntity binding, CancellationToken ct = default);
    Task<bool> DeleteBindingAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<PluginSchemaEntity>> GetSchemasAsync(CancellationToken ct = default);
    Task<PluginSchemaEntity?> GetSchemaAsync(string pluginId, int schemaVersion, CancellationToken ct = default);
    Task UpsertSchemaAsync(PluginSchemaEntity schema, CancellationToken ct = default);
}
