using Aneiang.Yarp.Dashboard.Storage;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>
/// Extension methods for registering <see cref="IDataStore"/> based on <see cref="StorageOptions"/>.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="IDataStore"/> singleton based on <c>Gateway:Storage</c> configuration section.
    /// If the section is missing, defaults to <see cref="StorageProvider.JsonFile"/>.
    /// </summary>
    public static IServiceCollection AddAneiangStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.SectionName);

        var storageOptions = new StorageOptions();
        configuration.GetSection(StorageOptions.SectionName).Bind(storageOptions);

        services.AddSingleton<IDataStore>(sp =>
        {
            var loggerBase = sp.GetRequiredService<ILoggerFactory>();

            return storageOptions.Provider switch
            {
                StorageProvider.Sqlite => new SqliteDataStore(storageOptions,
                    loggerBase.CreateLogger<SqliteDataStore>()),
                StorageProvider.Redis => new RedisDataStore(storageOptions,
                    loggerBase.CreateLogger<RedisDataStore>()),
                _ => new JsonFileDataStore(storageOptions,
                    loggerBase.CreateLogger<JsonFileDataStore>())
            };
        });

        return services;
    }
}
