using System.Text.Json;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;
using Aneiang.Yarp.Plugin.ProxyLog;
using Aneiang.Yarp.Plugin.ProxyLog.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;

/// <summary>
/// Manages hot-reloadable proxy-log settings, persisting overrides to SQLite so they
/// survive restarts and re-applying them to <see cref="ProxyLogRuntimeSettings"/> at startup.
/// </summary>
public sealed class LogSettingsService : IHostedService
{
    private const string SnapshotKey = "runtime_snapshot";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ILogSettingsRepository _repository;
    private readonly ProxyLogRuntimeSettings _runtime;
    private readonly ProxyLogPluginOptions _options;
    private readonly ILogger<LogSettingsService> _logger;

    public LogSettingsService(
        ILogSettingsRepository repository,
        ProxyLogRuntimeSettings runtime,
        IOptions<ProxyLogPluginOptions> options,
        ILogger<LogSettingsService> logger)
    {
        _repository = repository;
        _runtime = runtime;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var all = await _repository.LoadAllAsync(cancellationToken);
            if (all.TryGetValue(SnapshotKey, out var json) && !string.IsNullOrWhiteSpace(json))
            {
                var response = JsonSerializer.Deserialize<LogSettingsResponse>(json, JsonOptions);
                if (response != null)
                {
                    _runtime.Update(ToSnapshot(response));
                    _logger.LogInformation("Applied persisted proxy-log settings overrides.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load persisted proxy-log settings; using appsettings defaults.");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Gets the current runtime settings and whether they are customized.</summary>
    public async Task<LogSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var response = FromSnapshot(_runtime.Current);

        try
        {
            var all = await _repository.LoadAllAsync(cancellationToken);
            response.IsCustomized = all.ContainsKey(SnapshotKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read log settings customization flag.");
        }

        return response;
    }

    /// <summary>Applies a partial update, hot-reloads the runtime, and persists the override.</summary>
    public async Task<LogSettingsResponse> UpdateSettingsAsync(LogSettingsUpdateRequest request, CancellationToken cancellationToken)
    {
        var current = _runtime.Current;

        var next = new LogSettingsResponse
        {
            PersistenceEnabled = request.PersistenceEnabled ?? current.PersistenceEnabled,
            MetaRetentionDays = request.MetaRetentionDays ?? current.MetaRetentionDays,
            BodyRetentionDays = request.BodyRetentionDays ?? current.BodyRetentionDays,
            RequestBodyCaptureEnabled = request.RequestBodyCaptureEnabled ?? current.RequestBodyCaptureEnabled,
            ResponseBodyCaptureEnabled = request.ResponseBodyCaptureEnabled ?? current.ResponseBodyCaptureEnabled,
            MaxBodyLength = request.MaxBodyLength ?? current.MaxBodyLength,
            MaxBodyBufferBytes = request.MaxBodyBufferBytes ?? current.MaxBodyBufferBytes,
            SamplingEnabled = request.SamplingEnabled ?? current.SamplingEnabled,
            SamplingRate = request.SamplingRate ?? current.SamplingRate,
            ErrorsOnly = request.ErrorsOnly ?? current.ErrorsOnly,
            MinLogLevel = NormalizeLevelName(request.MinLogLevel) ?? ToLevelName(current.MinLogLevelNumeric),
            IsCustomized = true
        };

        next = Sanitize(next);
        _runtime.Update(ToSnapshot(next));

        await _repository.SaveAsync(SnapshotKey, JsonSerializer.Serialize(next, JsonOptions), cancellationToken);
        _logger.LogInformation("Proxy-log settings updated and persisted.");

        return next;
    }

    /// <summary>Reverts runtime settings to appsettings defaults and clears persisted overrides.</summary>
    public async Task<LogSettingsResponse> ResetSettingsAsync(CancellationToken cancellationToken)
    {
        var defaults = FromOptions();
        _runtime.Update(defaults);

        await _repository.ClearAsync(cancellationToken);
        _logger.LogInformation("Proxy-log settings reset to appsettings defaults.");

        return new LogSettingsResponse
        {
            PersistenceEnabled = defaults.PersistenceEnabled,
            MetaRetentionDays = defaults.MetaRetentionDays,
            BodyRetentionDays = defaults.BodyRetentionDays,
            RequestBodyCaptureEnabled = defaults.RequestBodyCaptureEnabled,
            ResponseBodyCaptureEnabled = defaults.ResponseBodyCaptureEnabled,
            MaxBodyLength = defaults.MaxBodyLength,
            MaxBodyBufferBytes = defaults.MaxBodyBufferBytes,
            SamplingEnabled = defaults.SamplingEnabled,
            SamplingRate = defaults.SamplingRate,
            ErrorsOnly = defaults.ErrorsOnly,
            MinLogLevel = ToLevelName(defaults.MinLogLevelNumeric),
            IsCustomized = false
        };
    }

    /// <summary>Builds the default snapshot from <c>ProxyLog</c> options.</summary>
    private ProxyLogRuntimeSnapshot FromOptions() => new(
        _options.LogPersistenceEnabled,
        _options.LogMetaRetentionDays,
        _options.LogBodyRetentionDays,
        _options.EnableProxyRequestBodyCapture,
        _options.EnableProxyResponseBodyCapture,
        _options.LogMaxBodyLength,
        _options.LogMaxBodyBufferBytes,
        _options.EnableLogSampling,
        _options.LogSamplingRate,
        _options.LogErrorsOnly,
        ParseLevel(_options.MinLogLevel));

    private static ProxyLogRuntimeSnapshot ToSnapshot(LogSettingsResponse response) => new(
        response.PersistenceEnabled,
        response.MetaRetentionDays,
        response.BodyRetentionDays,
        response.RequestBodyCaptureEnabled,
        response.ResponseBodyCaptureEnabled,
        response.MaxBodyLength,
        response.MaxBodyBufferBytes,
        response.SamplingEnabled,
        response.SamplingRate,
        response.ErrorsOnly,
        ParseLevel(response.MinLogLevel));

    private static LogSettingsResponse FromSnapshot(ProxyLogRuntimeSnapshot snapshot) => new()
    {
        PersistenceEnabled = snapshot.PersistenceEnabled,
        MetaRetentionDays = snapshot.MetaRetentionDays,
        BodyRetentionDays = snapshot.BodyRetentionDays,
        RequestBodyCaptureEnabled = snapshot.RequestBodyCaptureEnabled,
        ResponseBodyCaptureEnabled = snapshot.ResponseBodyCaptureEnabled,
        MaxBodyLength = snapshot.MaxBodyLength,
        MaxBodyBufferBytes = snapshot.MaxBodyBufferBytes,
        SamplingEnabled = snapshot.SamplingEnabled,
        SamplingRate = snapshot.SamplingRate,
        ErrorsOnly = snapshot.ErrorsOnly,
        MinLogLevel = ToLevelName(snapshot.MinLogLevelNumeric)
    };

    private static LogSettingsResponse Sanitize(LogSettingsResponse value)
    {
        value.MetaRetentionDays = Math.Clamp(value.MetaRetentionDays, 1, 365);
        value.BodyRetentionDays = Math.Clamp(value.BodyRetentionDays, 1, 365);
        value.MaxBodyLength = Math.Clamp(value.MaxBodyLength, 1024, 10 * 1024 * 1024);
        value.MaxBodyBufferBytes = Math.Clamp(value.MaxBodyBufferBytes, 1024, 10 * 1024 * 1024);
        value.SamplingRate = Math.Clamp(value.SamplingRate, 0.01, 1.0);
        value.MinLogLevel = NormalizeLevelName(value.MinLogLevel) ?? "Information";
        return value;
    }

    private static string? NormalizeLevelName(string? level) => level?.Trim() switch
    {
        "Trace" => "Trace",
        "Information" or "Info" => "Information",
        "Warning" or "Warn" => "Warning",
        "Error" => "Error",
        "Critical" or "Fatal" => "Critical",
        _ => null
    };

    private static string ToLevelName(int numeric) => numeric switch
    {
        4 => "Critical",
        3 => "Error",
        2 => "Warning",
        1 => "Information",
        _ => "Trace"
    };

    private static int ParseLevel(string? level) => level?.Trim() switch
    {
        "Critical" => 4,
        "Error" => 3,
        "Warning" => 2,
        "Information" => 1,
        _ => 0
    };
}
