namespace Aneiang.Yarp.Dashboard.Modules.Waf.Services;

/// <summary>
/// Interface for WAF settings persistence via structured storage.
/// </summary>
public interface IWafSettingsPersistenceService
{
    Task PreloadAsync(CancellationToken ct = default);
    WafSettingsData? Load();
    Task<WafSettingsData?> LoadAsync(CancellationToken ct = default);
    bool Save(WafSettingsData data);
    Task<bool> SaveAsync(WafSettingsData data, CancellationToken ct = default);
}
