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

        // Shared connection factory. Schema migration is triggered lazily on first
        // connection use (via Lazy<Task> in SqliteConnectionFactory), eliminating the
        // startup race condition that existed when migrator was a parallel IHostedService.
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<IDbConnectionFactory>(sp => sp.GetRequiredService<SqliteConnectionFactory>());

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
