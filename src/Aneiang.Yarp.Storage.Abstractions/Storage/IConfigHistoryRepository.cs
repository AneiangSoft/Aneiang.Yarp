namespace Aneiang.Yarp.Storage;

/// <summary>
/// Config history repository for configuration version management.
/// </summary>
public interface IConfigHistoryRepository
{
    Task<ConfigHistoryEntity?> GetConfigHistoryAsync(string versionId, CancellationToken ct = default);
    Task<IReadOnlyList<ConfigHistoryEntity>> GetConfigHistoryListAsync(int limit = 50, CancellationToken ct = default);
    Task SaveConfigHistoryAsync(ConfigHistoryEntity history, CancellationToken ct = default);
    Task DeleteConfigHistoryAsync(string versionId, CancellationToken ct = default);
    Task DeleteOldConfigHistoryAsync(int keepCount, CancellationToken ct = default);
    Task ClearConfigHistoryAsync(CancellationToken ct = default);
}
