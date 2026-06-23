using Aneiang.Yarp.Dashboard.Infrastructure.Alert;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure.HostedServices;

/// <summary>
/// Watches configuration files for changes and triggers a hot-reload of
/// <see cref="IConfigurationRoot"/>. Kestrel endpoint bindings are NOT reloaded
/// at runtime — they require process restart. Failures are caught and the
/// configuration is rolled back to the last good snapshot if enabled.
/// </summary>
public class ConfigurationFileWatcher : IHostedService, IAsyncDisposable
{
    private readonly IConfigurationRoot _configRoot;
    private readonly DeploymentOptions _options;
    private readonly IConfigSnapshotStore _snapshotStore;
    private readonly IGatewayAlertService _alertService;
    private readonly ILogger<ConfigurationFileWatcher> _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly string _basePath;
    private readonly string _env;
    private readonly Dictionary<string, DateTime> _lastFileWrites = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _debounceTimer;
    private Timer? _fallbackTimer;
    private readonly object _reloadLock = new();

    public ConfigurationFileWatcher(
        IConfiguration config,
        IOptions<DeploymentOptions> options,
        IConfigSnapshotStore snapshotStore,
        IGatewayAlertService alertService,
        IHostEnvironment hostEnvironment,
        ILogger<ConfigurationFileWatcher> logger)
    {
        _configRoot = config as IConfigurationRoot
            ?? throw new InvalidOperationException("IConfiguration must be IConfigurationRoot to support file watching");
        _options = options.Value;
        _snapshotStore = snapshotStore;
        _alertService = alertService;
        _logger = logger;
        _basePath = hostEnvironment.ContentRootPath;
        _env = hostEnvironment.EnvironmentName;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.HotReload.Enabled) return Task.CompletedTask;

        // Primary: FileSystemWatcher
        foreach (var pattern in _options.HotReload.WatchedFiles)
        {
            var fileName = ResolveFilePattern(pattern);
            var fullPath = Path.Combine(_basePath, fileName);
            if (!File.Exists(fullPath)) continue;

            try
            {
                var watcher = new FileSystemWatcher(_basePath, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
                };
                watcher.Changed += (s, e) => OnFileEvent(e.FullPath, "FileChange");
                watcher.Created += (s, e) => OnFileEvent(e.FullPath, "FileCreate");
                watcher.Renamed += (s, e) => OnFileEvent(e.FullPath, "FileRename");
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
                _logger.LogInformation("👁 Watching: {File}", fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to watch {File}, will rely on polling", fullPath);
            }
        }

        // Fallback: polling in case FileSystemWatcher misses events
        if (_options.HotReload.FallbackPollSeconds > 0)
        {
            _fallbackTimer = new Timer(_ => PollingCheck(), null,
                TimeSpan.FromSeconds(_options.HotReload.FallbackPollSeconds),
                TimeSpan.FromSeconds(_options.HotReload.FallbackPollSeconds));
        }

        return Task.CompletedTask;
    }

    private void OnFileEvent(string filePath, string trigger)
    {
        try { _lastFileWrites[filePath] = File.GetLastWriteTimeUtc(filePath); } catch { }
        ScheduleReload(filePath, trigger);
    }

    private void PollingCheck()
    {
        foreach (var pattern in _options.HotReload.WatchedFiles)
        {
            var fileName = ResolveFilePattern(pattern);
            var fullPath = Path.Combine(_basePath, fileName);
            if (!File.Exists(fullPath)) continue;
            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(fullPath);
                if (!_lastFileWrites.TryGetValue(fullPath, out var previousWrite))
                {
                    _lastFileWrites[fullPath] = lastWrite;
                    continue;
                }

                if (lastWrite > previousWrite)
                {
                    _lastFileWrites[fullPath] = lastWrite;
                    ScheduleReload(fullPath, "PollingFallback");
                }
            }
            catch { }
        }
    }

    private void ScheduleReload(string filePath, string trigger)
    {
        lock (_reloadLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ =>
            {
                try { ReloadConfig(filePath, trigger); }
                catch (Exception ex) { _logger.LogError(ex, "Reload callback failed"); }
            }, null, _options.HotReload.DebounceMilliseconds, Timeout.Infinite);
        }
    }

    private void ReloadConfig(string filePath, string trigger)
    {
        _logger.LogInformation("🔄 Hot-reloading from {File} (trigger={Trigger})", filePath, trigger);

        var snapshot = CaptureSnapshot(filePath, trigger);
        _ = _snapshotStore.SaveAsync(snapshot);

        try
        {
            _configRoot.Reload();
            _logger.LogInformation("Config hot-reloaded successfully");
            _alertService.AlertCustom("ConfigReloaded", "配置热更新",
                $"已从 {Path.GetFileName(filePath)} 重新加载", "Info");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Config reload failed, rolling back");
            _alertService.AlertCustom("ConfigReloadFailed", "配置重载失败", ex.Message, "Error");
            if (_options.HotReload.RollbackOnFailure) RestoreSnapshot(snapshot);
        }
    }

    private ConfigSnapshot CaptureSnapshot(string filePath, string trigger) => new()
    {
        Timestamp = DateTime.UtcNow,
        Trigger = trigger,
        FilePath = filePath,
        Data = _configRoot.GetSection("Gateway").Get<Dictionary<string, object?>>() ?? new(),
        RawContent = ReadFileContent(filePath)
    };

    private void RestoreSnapshot(ConfigSnapshot snapshot)
    {
        try
        {
            if (string.IsNullOrEmpty(snapshot.RawContent))
            {
                _logger.LogWarning("Snapshot has no raw content; skip rollback for {File}", snapshot.FilePath);
                return;
            }

            File.WriteAllText(snapshot.FilePath, snapshot.RawContent);
            _configRoot.Reload();
            _logger.LogInformation("Rolled back to {Timestamp}", snapshot.Timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback config snapshot");
        }
    }

    private static string? ReadFileContent(string filePath)
    {
        try { return File.Exists(filePath) ? File.ReadAllText(filePath) : null; }
        catch { return null; }
    }

    private string ResolveFilePattern(string pattern) =>
        pattern.Replace("{Environment}", _env, StringComparison.OrdinalIgnoreCase);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var w in _watchers) w.Dispose();
        _debounceTimer?.Dispose();
        _fallbackTimer?.Dispose();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var w in _watchers) w.Dispose();
        _debounceTimer?.Dispose();
        _fallbackTimer?.Dispose();
        return ValueTask.CompletedTask;
    }
}
