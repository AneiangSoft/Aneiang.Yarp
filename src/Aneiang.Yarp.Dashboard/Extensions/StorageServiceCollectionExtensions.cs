using Aneiang.Yarp.Dashboard.Infrastructure.Storage;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>
/// Extension methods for registering storage services based on <see cref="StorageOptions"/>.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Register storage services based on <c>Gateway:Storage</c> configuration section.
    /// Defaults to <see cref="StorageProvider.Sqlite"/> with structured schema.
    /// </summary>
    public static IServiceCollection AddAneiangStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.SectionName);

        var storageOptions = new StorageOptions();
        configuration.GetSection(StorageOptions.SectionName).Bind(storageOptions);

        // Register IGatewayRepository — the primary storage abstraction
        services.AddSingleton<IGatewayRepository>(sp =>
        {
            var loggerBase = sp.GetRequiredService<ILoggerFactory>();

            return storageOptions.Provider switch
            {
                StorageProvider.Redis => new RedisGatewayRepositoryPlaceholder(storageOptions,
                    loggerBase.CreateLogger<RedisGatewayRepositoryPlaceholder>()),
                _ => new SqliteGatewayRepository(storageOptions,
                    loggerBase.CreateLogger<SqliteGatewayRepository>())
            };
        });

        return services;
    }
}
