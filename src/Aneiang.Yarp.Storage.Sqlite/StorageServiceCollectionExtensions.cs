using Aneiang.Yarp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Storage.Sqlite;

/// <summary>
/// Extension methods for registering SQLite storage services.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Register SQLite storage repositories based on <c>Gateway:Storage</c> configuration section.
    /// </summary>
    public static IServiceCollection AddAneiangStorage(this IServiceCollection services)
    {
        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.SectionName);

        // Shared connection factory and deterministic schema migration
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddHostedService<SqliteSchemaMigrator>();

        // Individual repositories
        services.AddSingleton<IRouteRepository, SqliteRouteRepository>();
        services.AddSingleton<IClusterRepository, SqliteClusterRepository>();
        services.AddSingleton<IPolicyRepository, SqlitePolicyRepository>();
        services.AddSingleton<IConfigHistoryRepository, SqliteConfigHistoryRepository>();
        services.AddSingleton<IAuditLogRepository, SqliteAuditLogRepository>();
        services.AddSingleton<IWafSettingsRepository, SqliteWafSettingsRepository>();
        services.AddSingleton<INotificationRepository, SqliteNotificationRepository>();
        services.AddSingleton<IProxyLogRepository, SqliteProxyLogRepository>();
        services.AddSingleton<ILogSettingsRepository, SqliteLogSettingsRepository>();

        return services;
    }
}
