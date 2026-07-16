using Aneiang.Yarp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Storage.Sqlite;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddAneiangStorage(this IServiceCollection services)
    {
        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.SectionName);

        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<IDbConnectionFactory>(sp => sp.GetRequiredService<SqliteConnectionFactory>());

        services.AddSingleton<IRouteRepository, SqliteRouteRepository>();
        services.AddSingleton<IClusterRepository, SqliteClusterRepository>();
        services.AddSingleton<IPolicyRepository, SqlitePolicyRepository>();
        services.AddSingleton<IConfigHistoryRepository, SqliteConfigHistoryRepository>();
        services.AddSingleton<IAuditLogRepository, SqliteAuditLogRepository>();
        services.AddSingleton<IWafSettingsRepository, SqliteWafSettingsRepository>();
        services.AddSingleton<INotificationRepository, SqliteNotificationRepository>();
        services.AddSingleton<IProxyLogRepository, SqliteProxyLogRepository>();
        services.AddSingleton<ILogSettingsRepository, SqliteLogSettingsRepository>();
        services.AddSingleton<IAISettingsRepository, SqliteAISettingsRepository>();

        services.AddSingleton<IAIConversationRepository, SqliteAIConversationRepository>();
        services.AddSingleton<IAIAnalysisRepository, SqliteAIAnalysisRepository>();

        return services;
    }
}
