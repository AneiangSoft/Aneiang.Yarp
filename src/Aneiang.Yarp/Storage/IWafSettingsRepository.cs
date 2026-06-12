namespace Aneiang.Yarp.Storage;

/// <summary>
/// WAF settings repository for WAF configuration persistence.
/// </summary>
public interface IWafSettingsRepository
{
    Task<WafSettingsEntity?> GetWafSettingsAsync(CancellationToken ct = default);
    Task SaveWafSettingsAsync(WafSettingsEntity settings, CancellationToken ct = default);
}
