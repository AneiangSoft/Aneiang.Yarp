namespace Aneiang.Yarp.Storage;

/// <summary>
/// Repository interface for AI settings key-value storage.
/// Uses the ai_settings table in SQLite.
/// </summary>
public interface IAISettingsRepository
{
    /// <summary>Load all AI settings as key-value pairs from ai_settings.</summary>
    Task<Dictionary<string, string>> LoadAllAsync(CancellationToken ct = default);

    /// <summary>Save (upsert) a single key-value pair into ai_settings.</summary>
    Task SaveAsync(string key, string value, CancellationToken ct = default);

    /// <summary>Save multiple key-value pairs in a single transaction.</summary>
    Task SaveBatchAsync(IEnumerable<(string Key, string Value)> pairs, CancellationToken ct = default);

    /// <summary>Delete all rows from ai_settings.</summary>
    Task ClearAsync(CancellationToken ct = default);
}
