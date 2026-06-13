using Aneiang.Yarp.Dashboard.Infrastructure.Storage;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>
/// Extension methods for registering SQLite storage services.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Register SQLite storage repositories based on <c>Gateway:Storage</c> configuration section.
    /// </summary>
    public static IServiceCollection AddAneiangStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.SectionName);

        // Shared connection factory
        services.AddSingleton<SqliteConnectionFactory>();

        // Individual repositories
        services.AddSingleton<IRouteRepository, SqliteRouteRepository>();
        services.AddSingleton<IClusterRepository, SqliteClusterRepository>();
        services.AddSingleton<IPolicyRepository, SqlitePolicyRepository>();
        services.AddSingleton<IConfigHistoryRepository, SqliteConfigHistoryRepository>();
        services.AddSingleton<IAuditLogRepository, SqliteAuditLogRepository>();
        services.AddSingleton<IWafSettingsRepository, SqliteWafSettingsRepository>();
        services.AddSingleton<IProxyLogRepository, SqliteProxyLogRepository>();
        services.AddSingleton<INotificationRepository, SqliteNotificationRepository>();

        return services;
    }
}
