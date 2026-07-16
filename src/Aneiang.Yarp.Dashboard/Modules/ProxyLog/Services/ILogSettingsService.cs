using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Manages log settings with a 3-tier priority:
/// 1) SQLite proxy_log_settings table -> 2) IOptionsMonitor (appsettings) -> 3) DashboardOptions defaults.
/// </summary>
public interface ILogSettingsService
{
    /// <summary>Load current log settings (cached for 30 seconds).</summary>
    LogSettingsData Load();

    /// <summary>Async version of Load.</summary>
    Task<LogSettingsData> LoadAsync(CancellationToken ct = default);

    /// <summary>Update log settings and persist to SQLite.</summary>
    Task<LogSettingsData> SaveAsync(LogSettingsUpdateRequest request, CancellationToken ct = default);

    /// <summary>Reset log settings to defaults.</summary>
    Task<LogSettingsData> ResetAsync(CancellationToken ct = default);
}
