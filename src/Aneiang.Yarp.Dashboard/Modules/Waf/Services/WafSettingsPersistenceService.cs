using System.Text.Json;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Services;

public class WafSettingsPersistenceService : IWafSettingsPersistenceService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IWafSettingsRepository _wafRepo;
    private readonly ILogger<WafSettingsPersistenceService> _logger;
    private WafSettingsData? _cachedData;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private bool _initialized;

    public WafSettingsPersistenceService(IWafSettingsRepository wafRepo, ILogger<WafSettingsPersistenceService> logger)
    {
        _wafRepo = wafRepo;
        _logger = logger;
    }

    public async Task PreloadAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            _cachedData = await LoadInternalAsync(ct);
            _initialized = true;
        }
        finally { _cacheLock.Release(); }
    }

    private async Task<WafSettingsData?> LoadInternalAsync(CancellationToken ct)
    {
        try
        {
            var entity = await _wafRepo.GetWafSettingsAsync(ct);
            if (entity == null) return null;

            return new WafSettingsData
            {
                Enabled = entity.Enabled,
                EnableIpCheck = entity.EnableIpCheck,
                IpWhitelist = ParseList(entity.IpWhitelistJson),
                IpBlacklist = ParseList(entity.IpBlacklistJson),
                EnableRequestSizeValidation = entity.EnableRequestSizeValidation,
                MaxRequestBodySize = entity.MaxRequestBodySize,
                MaxHeaderCount = entity.MaxHeaderCount,
                MaxHeaderSize = entity.MaxHeaderSize,
                EnableSqlInjectionDetection = entity.EnableSqlInjectionDetection,
                EnableXssDetection = entity.EnableXssDetection,
                EnablePathTraversalDetection = entity.EnablePathTraversalDetection
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load WAF settings from repository");
            return null;
        }
    }

    private static List<string> ParseList(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json, _jsonOptions) ?? []; }
        catch { return []; }
    }

    public WafSettingsData? Load()
    {
        if (_initialized && _cachedData != null) return _cachedData;
        return Task.Run(async () => await LoadInternalAsync(CancellationToken.None)).GetAwaiter().GetResult();
    }

    public async Task<WafSettingsData?> LoadAsync(CancellationToken ct = default)
    {
        if (_initialized && _cachedData != null) return _cachedData;
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_initialized && _cachedData != null) return _cachedData;
            _cachedData = await LoadInternalAsync(ct);
            _initialized = true;
            return _cachedData;
        }
        finally { _cacheLock.Release(); }
    }

    public bool Save(WafSettingsData data)
    {
        return Task.Run(async () => await SaveCoreAsync(data, CancellationToken.None)).GetAwaiter().GetResult();
    }

    public async Task<bool> SaveAsync(WafSettingsData data, CancellationToken ct = default)
    {
        return await SaveCoreAsync(data, ct);
    }

    private async Task<bool> SaveCoreAsync(WafSettingsData data, CancellationToken ct)
    {
        try
        {
            var entity = new WafSettingsEntity
            {
                Enabled = data.Enabled,
                EnableIpCheck = data.EnableIpCheck,
                IpWhitelistJson = data.IpWhitelist.Count > 0 ? JsonSerializer.Serialize(data.IpWhitelist, _jsonOptions) : null,
                IpBlacklistJson = data.IpBlacklist.Count > 0 ? JsonSerializer.Serialize(data.IpBlacklist, _jsonOptions) : null,
                EnableRequestSizeValidation = data.EnableRequestSizeValidation,
                MaxRequestBodySize = data.MaxRequestBodySize,
                MaxHeaderCount = data.MaxHeaderCount,
                MaxHeaderSize = data.MaxHeaderSize,
                EnableSqlInjectionDetection = data.EnableSqlInjectionDetection,
                EnableXssDetection = data.EnableXssDetection,
                EnablePathTraversalDetection = data.EnablePathTraversalDetection,
                UpdatedAt = DateTime.Now
            };

            await _wafRepo.SaveWafSettingsAsync(entity, ct);
            _cachedData = data;
            _logger.LogDebug("WAF settings saved to repository");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save WAF settings");
            return false;
        }
    }
}
