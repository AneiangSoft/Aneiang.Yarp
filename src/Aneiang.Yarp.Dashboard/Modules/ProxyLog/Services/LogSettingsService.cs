using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Manages log settings with a 3-tier priority: 1) SQLite proxy_log_settings table → 2) IOptionsMonitor (appsettings) → 3) DashboardOptions defaults.
/// Settings reads are cached in IMemoryCache for 30 seconds.
/// Writes update SQLite via ILogSettingsRepository and clear cache.
/// </summary>
public class LogSettingsService
{
    private readonly ILogSettingsRepository _logSettingsRepo;
    private readonly IOptionsMonitor<DashboardOptions> _optionsMonitor;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LogSettingsService> _logger;
    private const string CacheKey = "dashboard:log-settings";

    public LogSettingsService(
        ILogSettingsRepository logSettingsRepo,
        IOptionsMonitor<DashboardOptions> optionsMonitor,
        IMemoryCache cache,
        ILogger<LogSettingsService> logger)
    {
        _logSettingsRepo = logSettingsRepo;
        _optionsMonitor = optionsMonitor;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Load current log settings. Priority: SQLite → IOptionsMonitor → defaults.
    /// Result is cached for 30 seconds.
    /// </summary>
    public LogSettingsData Load()
    {
        return _cache.GetOrCreate(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return LoadInternal();
        })!;
    }

    /// <summary>
    /// Async version of Load.
    /// </summary>
    public async Task<LogSettingsData> LoadAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out LogSettingsData? cached) && cached != null)
            return cached;

        var data = await LoadInternalAsync(ct);
        _cache.Set(CacheKey, data, TimeSpan.FromSeconds(30));
        return data;
    }

    /// <summary>
    /// Save log settings. Updates SQLite, clears cache.
    /// </summary>
    public async Task<LogSettingsData> SaveAsync(LogSettingsUpdateRequest request, CancellationToken ct = default)
    {
        var current = await LoadAsync(ct);

        // Apply updates to current data
        if (request.LogPersistenceEnabled.HasValue) current.LogPersistenceEnabled = request.LogPersistenceEnabled.Value;
        if (request.LogMetaRetentionDays.HasValue)
            current.LogMetaRetentionDays = Math.Clamp(request.LogMetaRetentionDays.Value, 1, 365);
        if (request.LogBodyRetentionDays.HasValue)
            current.LogBodyRetentionDays = Math.Clamp(request.LogBodyRetentionDays.Value, 1, current.LogMetaRetentionDays);
        if (request.EnableProxyRequestBodyCapture.HasValue) current.EnableProxyRequestBodyCapture = request.EnableProxyRequestBodyCapture.Value;
        if (request.EnableProxyResponseBodyCapture.HasValue) current.EnableProxyResponseBodyCapture = request.EnableProxyResponseBodyCapture.Value;
        if (request.LogMaxBodyLength.HasValue) current.LogMaxBodyLength = Math.Clamp(request.LogMaxBodyLength.Value, 256, 1048576);
        if (request.EnableLogSampling.HasValue) current.EnableLogSampling = request.EnableLogSampling.Value;
        if (request.LogSamplingRate.HasValue) current.LogSamplingRate = Math.Clamp(request.LogSamplingRate.Value, 0.0, 1.0);
        if (request.LogErrorsOnly.HasValue) current.LogErrorsOnly = request.LogErrorsOnly.Value;
        if (request.MinLogLevel != null) current.MinLogLevel = request.MinLogLevel;
        if (request.LogBufferCapacity.HasValue) current.LogBufferCapacity = Math.Max(16, request.LogBufferCapacity.Value);

        // Ensure body ≤ meta
        current.LogBodyRetentionDays = Math.Min(current.LogBodyRetentionDays, current.LogMetaRetentionDays);

        // Write to SQLite
        await WriteOverridesAsync(current, ct);

        // Clear cache so next Load reads fresh data
        _cache.Remove(CacheKey);

        _logger.LogInformation("Log settings updated: persistence={Persistence}, meta={MetaDays}d, body={BodyDays}d, reqBody={ReqBody}, resBody={ResBody}",
            current.LogPersistenceEnabled, current.LogMetaRetentionDays, current.LogBodyRetentionDays,
            current.EnableProxyRequestBodyCapture, current.EnableProxyResponseBodyCapture);

        return current;
    }

    /// <summary>
    /// Reset all log settings to DashboardOptions defaults and clear SQLite overrides.
    /// </summary>
    public async Task<LogSettingsData> ResetAsync(CancellationToken ct = default)
    {
        var defaults = GetDefaults();

        // Clear all overrides from SQLite
        await _logSettingsRepo.ClearAsync(ct);

        // Clear cache
        _cache.Remove(CacheKey);

        _logger.LogInformation("Log settings reset to defaults");
        return defaults;
    }

    private LogSettingsData LoadInternal()
    {
        return Task.Run(async () => await LoadInternalAsync(CancellationToken.None)).GetAwaiter().GetResult();
    }

    private async Task<LogSettingsData> LoadInternalAsync(CancellationToken ct)
    {
        var defaults = GetDefaults();

        try
        {
            var overrides = await _logSettingsRepo.LoadAllAsync(ct);

            // Apply SQLite overrides onto defaults
            if (overrides.TryGetValue("LogPersistenceEnabled", out var persistVal))
                defaults.LogPersistenceEnabled = persistVal == "true";
            if (overrides.TryGetValue("LogMetaRetentionDays", out var metaDays))
                defaults.LogMetaRetentionDays = int.TryParse(metaDays, out var md) ? Math.Clamp(md, 1, 365) : defaults.LogMetaRetentionDays;
            if (overrides.TryGetValue("LogBodyRetentionDays", out var bodyDays))
                defaults.LogBodyRetentionDays = int.TryParse(bodyDays, out var bd) ? Math.Clamp(bd, 1, defaults.LogMetaRetentionDays) : defaults.LogBodyRetentionDays;
            if (overrides.TryGetValue("EnableProxyRequestBodyCapture", out var reqCap))
                defaults.EnableProxyRequestBodyCapture = reqCap == "true";
            if (overrides.TryGetValue("EnableProxyResponseBodyCapture", out var resCap))
                defaults.EnableProxyResponseBodyCapture = resCap == "true";
            if (overrides.TryGetValue("LogMaxBodyLength", out var maxBody))
                defaults.LogMaxBodyLength = int.TryParse(maxBody, out var ml) ? Math.Clamp(ml, 256, 1048576) : defaults.LogMaxBodyLength;
            if (overrides.TryGetValue("EnableLogSampling", out var sampling))
                defaults.EnableLogSampling = sampling == "true";
            if (overrides.TryGetValue("LogSamplingRate", out var rate))
                defaults.LogSamplingRate = double.TryParse(rate, out var sr) ? Math.Clamp(sr, 0.0, 1.0) : defaults.LogSamplingRate;
            if (overrides.TryGetValue("LogErrorsOnly", out var errorsOnly))
                defaults.LogErrorsOnly = errorsOnly == "true";
            if (overrides.TryGetValue("MinLogLevel", out var minLevel))
                defaults.MinLogLevel = minLevel;
            if (overrides.TryGetValue("LogBufferCapacity", out var capacity))
                defaults.LogBufferCapacity = int.TryParse(capacity, out var cap) ? Math.Max(16, cap) : defaults.LogBufferCapacity;

            // Enforce body ≤ meta
            defaults.LogBodyRetentionDays = Math.Min(defaults.LogBodyRetentionDays, defaults.LogMetaRetentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load log settings from SQLite, using IOptionsMonitor defaults");
        }

        return defaults;
    }

    private LogSettingsData GetDefaults()
    {
        var opts = _optionsMonitor.CurrentValue;
        return new LogSettingsData
        {
            LogPersistenceEnabled = opts.LogPersistenceEnabled,
            LogMetaRetentionDays = opts.LogMetaRetentionDays,
            LogBodyRetentionDays = opts.LogBodyRetentionDays,
            EnableProxyRequestBodyCapture = opts.EnableProxyRequestBodyCapture,
            EnableProxyResponseBodyCapture = opts.EnableProxyResponseBodyCapture,
            LogMaxBodyLength = opts.LogMaxBodyLength,
            EnableLogSampling = opts.EnableLogSampling,
            LogSamplingRate = opts.LogSamplingRate,
            LogErrorsOnly = opts.LogErrorsOnly,
            MinLogLevel = opts.MinLogLevel,
            LogBufferCapacity = opts.LogBufferCapacity,
            LogMaxBodyBufferBytes = opts.LogMaxBodyBufferBytes
        };
    }

    private async Task WriteOverridesAsync(LogSettingsData data, CancellationToken ct)
    {
        try
        {
            var pairs = new (string Key, string Value)[]
            {
                ("LogPersistenceEnabled", data.LogPersistenceEnabled.ToString().ToLowerInvariant()),
                ("LogMetaRetentionDays", data.LogMetaRetentionDays.ToString()),
                ("LogBodyRetentionDays", data.LogBodyRetentionDays.ToString()),
                ("EnableProxyRequestBodyCapture", data.EnableProxyRequestBodyCapture.ToString().ToLowerInvariant()),
                ("EnableProxyResponseBodyCapture", data.EnableProxyResponseBodyCapture.ToString().ToLowerInvariant()),
                ("LogMaxBodyLength", data.LogMaxBodyLength.ToString()),
                ("EnableLogSampling", data.EnableLogSampling.ToString().ToLowerInvariant()),
                ("LogSamplingRate", data.LogSamplingRate.ToString("F2")),
                ("LogErrorsOnly", data.LogErrorsOnly.ToString().ToLowerInvariant()),
                ("MinLogLevel", data.MinLogLevel),
                ("LogBufferCapacity", data.LogBufferCapacity.ToString())
            };

            await _logSettingsRepo.SaveBatchAsync(pairs, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write log settings to SQLite");
            throw;
        }
    }
}

/// <summary>
/// Data model for log settings as returned to the UI.
/// </summary>
public class LogSettingsData
{
    public bool LogPersistenceEnabled { get; set; }
    public int LogMetaRetentionDays { get; set; }
    public int LogBodyRetentionDays { get; set; }
    public bool EnableProxyRequestBodyCapture { get; set; }
    public bool EnableProxyResponseBodyCapture { get; set; }
    public int LogMaxBodyLength { get; set; }
    public bool EnableLogSampling { get; set; }
    public double LogSamplingRate { get; set; }
    public bool LogErrorsOnly { get; set; }
    public string MinLogLevel { get; set; } = "Debug";
    public int LogBufferCapacity { get; set; }
    public int LogMaxBodyBufferBytes { get; set; }

    /// <summary>Whether settings require a restart to take full effect.</summary>
    public bool RequiresRestart => false;
}
