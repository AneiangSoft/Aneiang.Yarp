using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Storage;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// In-memory snapshot list with persistence via <see cref="IConfigHistoryRepository"/>.
/// Extracted from <see cref="ConfigPersistenceService"/> for single responsibility.
/// </summary>
internal sealed class ConfigSnapshotManager
{
    private readonly IConfigHistoryRepository _historyRepo;
    private readonly IOptionsMonitor<ConfigHistoryOptions> _options;
    private readonly ILogger _logger;
    private readonly Func<Task<JsonElement>> _exportConfig;
    private readonly List<ConfigSnapshot> _history = new();
    private readonly object _historyLock = new();
    private bool _historyLoaded;

    public ConfigSnapshotManager(
        IConfigHistoryRepository historyRepo,
        IOptionsMonitor<ConfigHistoryOptions> options,
        ILogger logger,
        Func<Task<JsonElement>> exportConfig)
    {
        _historyRepo = historyRepo;
        _options = options;
        _logger = logger;
        _exportConfig = exportConfig;
    }

    private int GetMaxSnapshots() => Math.Max(1, _options.CurrentValue.MaxSnapshots);

    private async Task EnsureHistoryLoadedAsync()
    {
        if (_historyLoaded) return;
        _historyLoaded = true;

        try
        {
            var entities = await _historyRepo.GetConfigHistoryListAsync(GetMaxSnapshots());
            lock (_historyLock)
            {
                foreach (var entity in entities)
                    _history.Add(entity.ToConfigSnapshot());
                _logger.LogDebug("Loaded {Count} snapshots from repository", entities.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load persisted snapshots");
        }
    }

    public async Task<ConfigSnapshot> SaveSnapshotAsync(string? description = null, string? clientIp = null)
    {
        await EnsureHistoryLoadedAsync();

        var config = await _exportConfig();

        var snapshot = new ConfigSnapshot
        {
            Description = description ?? "Manual snapshot",
            ClientIp = clientIp,
            Config = config
        };

        var maxSnapshots = GetMaxSnapshots();
        lock (_historyLock)
        {
            _history.Add(snapshot);
            while (_history.Count > maxSnapshots)
                _history.RemoveAt(0);
        }

        var entity = snapshot.ToEntity("dashboard-user");
        await _historyRepo.SaveConfigHistoryAsync(entity);
        await _historyRepo.DeleteOldConfigHistoryAsync(maxSnapshots);

        _logger.LogInformation("Snapshot saved: {VersionId}, Description: {Description}",
            snapshot.VersionId, snapshot.Description);
        return snapshot;
    }

    public async Task<IReadOnlyList<ConfigSnapshot>> GetHistoryAsync()
    {
        await EnsureHistoryLoadedAsync();
        lock (_historyLock) { return _history.ToList().AsReadOnly(); }
    }

    public IReadOnlyList<ConfigSnapshot> GetHistory()
    {
        if (_historyLoaded && _history.Count > 0)
            lock (_historyLock) { return _history.ToList().AsReadOnly(); }
        return Array.Empty<ConfigSnapshot>();
    }

    public async Task ClearHistoryAsync()
    {
        await EnsureHistoryLoadedAsync();
        lock (_historyLock) { _history.Clear(); }
        await _historyRepo.ClearConfigHistoryAsync();
        _logger.LogInformation("Configuration history cleared");
    }

    public async Task<ConfigSnapshot?> FindSnapshotAsync(string versionId)
    {
        await EnsureHistoryLoadedAsync();
        lock (_historyLock) { return _history.FirstOrDefault(s => s.VersionId == versionId); }
    }
}
