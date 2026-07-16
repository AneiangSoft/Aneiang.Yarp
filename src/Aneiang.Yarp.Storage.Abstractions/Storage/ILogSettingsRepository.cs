namespace Aneiang.Yarp.Storage;

public interface ILogSettingsRepository
{
    Task<Dictionary<string, string>> LoadAllAsync(CancellationToken ct = default);

    Task SaveAsync(string key, string value, CancellationToken ct = default);

    Task SaveBatchAsync(IEnumerable<(string Key, string Value)> pairs, CancellationToken ct = default);

    Task ClearAsync(CancellationToken ct = default);
}
