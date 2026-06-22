using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Aneiang.Yarp.Dashboard.Infrastructure.HostedServices;

namespace Aneiang.Yarp.Dashboard.Infrastructure.HostedServices;

/// <summary>
/// Extension methods for registering background (non-blocking) hosted services.
/// </summary>
internal static class BackgroundHostedServiceExtensions
{
    /// <summary>
    /// Registers a hosted service that runs its StartAsync in a background task,
    /// so it does not block Kestrel from binding ports.
    /// </summary>
    public static IServiceCollection AddBackgroundHostedService<TService>(this IServiceCollection services)
        where TService : class, IHostedService
    {
        services.AddSingleton<TService>();
        services.AddHostedService(sp => new BackgroundHostedServiceWrapper(
            sp.GetRequiredService<TService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BackgroundHostedServiceWrapper>>()));
        return services;
    }

    /// <summary>
    /// Registers a hosted service wrapped with startup timing diagnostics.
    /// Use this to identify which service is hanging during host startup.
    /// </summary>
    public static IServiceCollection AddTimedHostedService<TService>(this IServiceCollection services)
        where TService : class, IHostedService
    {
        services.AddSingleton<TService>();
        services.AddHostedService(sp => new TimedHostedService(
            sp.GetRequiredService<TService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TimedHostedService>>()));
        return services;
    }
}
