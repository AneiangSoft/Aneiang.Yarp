namespace Aneiang.Yarp.Storage;

/// <summary>
/// Repository interface for log settings key-value storage.
/// Uses the proxy_log_settings table in SQLite.
/// </summary>
public interface ILogSettingsRepository
{
    /// <summary>Load all settings as key-value pairs from proxy_log_settings.</summary>
    Task<Dictionary<string, string>> LoadAllAsync(CancellationToken ct = default);

    /// <summary>Save (upsert) a key-value pair into proxy_log_settings.</summary>
    Task SaveAsync(string key, string value, CancellationToken ct = default);

    /// <summary>Save multiple key-value pairs in a single transaction.</summary>
    Task SaveBatchAsync(IEnumerable<(string Key, string Value)> pairs, CancellationToken ct = default);

    /// <summary>Delete all rows from proxy_log_settings.</summary>
    Task ClearAsync(CancellationToken ct = default);
}
