using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Deployment;

/// <summary>
/// Snapshot of the configuration at a point in time. Used for rollback on hot-reload failure.
/// </summary>
public class ConfigSnapshot
{
    /// <summary>When the snapshot was captured.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Trigger source: "FileChange" / "PollingFallback" / "Manual" / "API".</summary>
    public string Trigger { get; set; } = string.Empty;

    /// <summary>Absolute path of the config file that triggered the snapshot.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Captured configuration tree under the "Gateway" section.</summary>
    public Dictionary<string, object?> Data { get; set; } = new();

    /// <summary>Raw content of the changed configuration file, used for safe rollback.</summary>
    public string? RawContent { get; set; }
}

/// <summary>
/// Storage abstraction for configuration snapshots.
/// </summary>
public interface IConfigSnapshotStore
{
    /// <summary>Persist a new snapshot. Triggers retention cleanup of older entries.</summary>
    Task SaveAsync(ConfigSnapshot snapshot);

    /// <summary>Get the most recent snapshot, or null if no snapshots exist.</summary>
    Task<ConfigSnapshot?> GetLatestAsync();

    /// <summary>Get the most recent N snapshots ordered newest first.</summary>
    Task<List<ConfigSnapshot>> GetHistoryAsync(int count = 5);

    /// <summary>Get the snapshot for a given timestamp (best match), or null.</summary>
    Task<ConfigSnapshot?> RestoreAsync(DateTime timestamp);
}

/// <summary>
/// File-system implementation of <see cref="IConfigSnapshotStore"/>.
/// Snapshots are stored as JSON files in <c>&lt;base&gt;/.config-snapshots/</c>.
/// </summary>
public class FileConfigSnapshotStore : IConfigSnapshotStore
{
    private readonly string _snapshotDir;
    private readonly int _maxSnapshots;
    private readonly ILogger<FileConfigSnapshotStore> _logger;

    public FileConfigSnapshotStore(IOptions<DeploymentOptions> options, ILogger<FileConfigSnapshotStore> logger)
    {
        _snapshotDir = Path.Combine(AppContext.BaseDirectory, ".config-snapshots");
        Directory.CreateDirectory(_snapshotDir);
        _maxSnapshots = options.Value.HotReload.MaxSnapshots;
        _logger = logger;
    }

    public async Task SaveAsync(ConfigSnapshot snapshot)
    {
        try
        {
            var fileName = $"{snapshot.Timestamp:yyyyMMddHHmmssfff}-{Sanitize(snapshot.Trigger)}.json";
            var path = Path.Combine(_snapshotDir, fileName);
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
            CleanupOldSnapshots();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save config snapshot");
        }
    }

    public async Task<ConfigSnapshot?> GetLatestAsync()
    {
        var file = Directory.GetFiles(_snapshotDir, "*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();
        return await ReadSnapshotAsync(file);
    }

    public async Task<List<ConfigSnapshot>> GetHistoryAsync(int count = 5)
    {
        var files = Directory.GetFiles(_snapshotDir, "*.json")
            .OrderByDescending(f => f)
            .Take(count)
            .ToList();
        var results = new List<ConfigSnapshot>();
        foreach (var f in files)
        {
            var snap = await ReadSnapshotAsync(f);
            if (snap != null) results.Add(snap);
        }
        return results;
    }

    public async Task<ConfigSnapshot?> RestoreAsync(DateTime timestamp)
    {
        var prefix = timestamp.ToString("yyyyMMddHHmmssfff");
        var file = Directory.GetFiles(_snapshotDir, "*.json")
            .FirstOrDefault(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.Ordinal));
        return await ReadSnapshotAsync(file);
    }

    private void CleanupOldSnapshots()
    {
        if (_maxSnapshots <= 0) return;
        try
        {
            var files = Directory.GetFiles(_snapshotDir, "*.json")
                .OrderByDescending(f => f)
                .Skip(_maxSnapshots)
                .ToList();
            foreach (var f in files)
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up old snapshots");
        }
    }

    private static async Task<ConfigSnapshot?> ReadSnapshotAsync(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<ConfigSnapshot>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string Sanitize(string input) =>
        string.IsNullOrEmpty(input) ? "unknown" : input.Replace('/', '_').Replace('\\', '_');
}
