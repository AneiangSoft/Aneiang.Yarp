namespace Aneiang.Yarp.Storage;

public interface IWafSettingsRepository
{
    Task<WafSettingsEntity?> GetWafSettingsAsync(CancellationToken ct = default);
    Task SaveWafSettingsAsync(WafSettingsEntity settings, CancellationToken ct = default);
}
