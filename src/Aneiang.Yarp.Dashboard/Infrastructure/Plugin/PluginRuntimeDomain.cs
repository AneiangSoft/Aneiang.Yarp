using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Yarp.ReverseProxy.Model;
using Aneiang.Yarp.Plugins;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

public interface IPluginRuntimeDomainManager
{
    PluginRuntimeDomain Current { get; }
    Task<PluginRuntimeDomainPreparation> PrepareAsync(
        IReadOnlyCollection<string> enabledPluginIds,
        CancellationToken cancellationToken = default);
    Task<PluginRuntimeTransitionResult> TransitionAsync(
        IReadOnlyCollection<string> enabledPluginIds,
        CancellationToken cancellationToken = default);
}

public sealed record PluginRuntimeTransitionResult(bool Succeeded, string? Error = null);

public sealed class PluginRuntimeDomain : IAsyncDisposable
{
    private readonly IReadOnlyList<IAsyncDisposable> _externalLoads;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _activeRequests;
    private int _retired;
    private int _disposed;

    internal PluginRuntimeDomain(
        IServiceProvider services,
        IReadOnlyDictionary<string, IGatewayPlugin> plugins,
        RequestDelegate middleware,
        RequestDelegate proxyPipeline,
        IReadOnlyList<IAsyncDisposable> externalLoads,
        PluginDashboardBuilder? dashboardBuilder = null)
    {
        Services = services;
        Plugins = plugins;
        Middleware = middleware;
        ProxyPipeline = proxyPipeline;
        _externalLoads = externalLoads;
        DashboardBuilder = dashboardBuilder ?? new PluginDashboardBuilder();
    }

    public IServiceProvider Services { get; }
    public IReadOnlyDictionary<string, IGatewayPlugin> Plugins { get; }
    public RequestDelegate Middleware { get; }
    public RequestDelegate ProxyPipeline { get; }
    public PluginDashboardBuilder DashboardBuilder { get; }

    public bool TryAcquire(out PluginRuntimeDomainLease lease)
    {
        lease = default;
        if (Volatile.Read(ref _retired) != 0)
            return false;

        Interlocked.Increment(ref _activeRequests);
        if (Volatile.Read(ref _retired) == 0)
        {
            lease = new PluginRuntimeDomainLease(this);
            return true;
        }

        Release();
        return false;
    }

    internal async ValueTask RetireAndDisposeAsync()
    {
        if (Interlocked.Exchange(ref _retired, 1) == 0 && Volatile.Read(ref _activeRequests) == 0)
            _drained.TrySetResult();

        await _drained.Task;
        await DisposeAsync();
    }

    internal void Release()
    {
        if (Interlocked.Decrement(ref _activeRequests) == 0 && Volatile.Read(ref _retired) != 0)
            _drained.TrySetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (Services is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (Services is IDisposable disposable)
            disposable.Dispose();

        foreach (var load in _externalLoads.Reverse())
            await load.DisposeAsync();
    }

    internal static PluginRuntimeDomain Empty => new(
        new ServiceCollection().BuildServiceProvider(),
        new Dictionary<string, IGatewayPlugin>(StringComparer.OrdinalIgnoreCase),
        _ => Task.CompletedTask,
        _ => Task.CompletedTask,
        []);
}

public readonly struct PluginRuntimeDomainLease : IDisposable
{
    private readonly PluginRuntimeDomain? _domain;

    internal PluginRuntimeDomainLease(PluginRuntimeDomain domain) => _domain = domain;

    public void Dispose() => _domain?.Release();
}

public sealed class PluginRuntimeDomainPreparation : IAsyncDisposable
{
    private readonly PluginRuntimeDomainManager _owner;
    private PluginRuntimeDomain? _candidate;
    private int _committed;

    internal PluginRuntimeDomainPreparation(PluginRuntimeDomainManager owner, PluginRuntimeDomain candidate)
    {
        _owner = owner;
        _candidate = candidate;
    }

    public PluginRuntimeDomain Candidate => _candidate ?? throw new ObjectDisposedException(nameof(PluginRuntimeDomainPreparation));

    public async Task<PluginHealthProbeResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        foreach (var plugin in Candidate.Plugins.Values)
        {
            if (plugin is not IPluginHealthProbe probe)
                continue;

            var result = await probe.CheckHealthAsync(cancellationToken);
            if (result.Status == PluginHealthStatus.Unhealthy)
                return result;
        }

        return new PluginHealthProbeResult(PluginHealthStatus.Healthy, "Plugin runtime domain is ready.", DateTimeOffset.UtcNow);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = _candidate ?? throw new ObjectDisposedException(nameof(PluginRuntimeDomainPreparation));
        if (Interlocked.Exchange(ref _committed, 1) != 0)
            throw new InvalidOperationException("The plugin runtime domain preparation was already committed.");

        _candidate = null;
        await _owner.CommitAsync(candidate);
    }

    public async ValueTask DisposeAsync()
    {
        var candidate = Interlocked.Exchange(ref _candidate, null);
        if (candidate != null && Volatile.Read(ref _committed) == 0)
            await candidate.DisposeAsync();
    }
}

public sealed class PluginRuntimeDomainInitializer(
    IGatewayPluginManager plugins,
    IPluginRuntimeDomainManager runtimeDomains,
    IPluginConfigurationRepository pluginConfigRepository,
    ILogger<PluginRuntimeDomainInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Clean up stale plugin bindings for plugins that no longer exist
        try
        {
            var knownIds = plugins.GetAllManifests().Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var bindings = await pluginConfigRepository.GetBindingsAsync(cancellationToken);
            var stale = bindings.Where(b => !knownIds.Contains(b.PluginId)).ToArray();
            if (stale.Length > 0)
            {
                foreach (var s in stale)
                {
                    logger.LogWarning("Removing stale binding '{BindingId}' for removed plugin '{PluginId}'.", s.Id, s.PluginId);
                    await pluginConfigRepository.DeleteBindingAsync(s.Id, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up stale plugin bindings during startup.");
        }

        var enabled = plugins.GetAllManifests()
            .Where(manifest => plugins.IsPluginEnabled(manifest.Id))
            .Select(manifest => manifest.Id)
            .ToArray();
        var result = await runtimeDomains.TransitionAsync(enabled, cancellationToken);
        if (!result.Succeeded)
            logger.LogError("Initial plugin runtime domain could not be activated: {Error}", result.Error);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class PluginRuntimeDomainManager : IPluginRuntimeDomainManager, IAsyncDisposable
{
    private static readonly object PipelineContinuationKey = new();
    private static readonly string[] ProxyPluginOrder =
        ["rate-limit", "circuit-breaker", "request-retry", "response-cache", "proxy-log", "traffic-metrics", "cluster-metrics"];

    private readonly IServiceProvider _rootServices;
    private readonly IReadOnlyDictionary<string, IGatewayPlugin> _firstPartyPlugins;
    private readonly ExternalGatewayPluginHost _externalHost;
    private readonly ILogger<PluginRuntimeDomainManager> _logger;
    private PluginRuntimeDomain _current = PluginRuntimeDomain.Empty;

    private const int PostTransitionWatchMinimumRequests = 10;
    private const double PostTransitionWatchErrorRateThreshold = 0.05;
    private static readonly TimeSpan PostTransitionWatchWindow = TimeSpan.FromSeconds(30);
    private int _postTransitionWatchRunning;

    public PluginRuntimeDomainManager(
        IServiceProvider rootServices,
        ExternalGatewayPluginHost externalHost,
        ILogger<PluginRuntimeDomainManager> logger)
    {
        _rootServices = rootServices;
        _firstPartyPlugins = rootServices.GetServices<IGatewayPlugin>()
            .ToDictionary(x => x.PluginId, StringComparer.OrdinalIgnoreCase);
        _externalHost = externalHost;
        _logger = logger;
    }

    public PluginRuntimeDomain Current => Volatile.Read(ref _current);

    public Task InvokeMiddlewareAsync(HttpContext context, RequestDelegate next) =>
        InvokeCurrentPipelineAsync(context, domain => domain.Middleware, next);

    public Task InvokeProxyPipelineAsync(HttpContext context, RequestDelegate next) =>
        InvokeCurrentPipelineAsync(context, domain => domain.ProxyPipeline, next);

    public async Task<PluginRuntimeDomainPreparation> PrepareAsync(
        IReadOnlyCollection<string> enabledPluginIds,
        CancellationToken cancellationToken = default)
    {
        var enabled = enabledPluginIds
            .Where(pluginId => !NativePluginAdapters.IsNative(pluginId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plugins = new Dictionary<string, IGatewayPlugin>(StringComparer.OrdinalIgnoreCase);
        var externalLoads = new List<IAsyncDisposable>();

        try
        {
            foreach (var pluginId in enabled)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_firstPartyPlugins.TryGetValue(pluginId, out var firstParty))
                {
                    plugins.Add(pluginId, firstParty);
                    continue;
                }

                try
                {
                    if (!_externalHost.HasManifest(pluginId))
                    {
                        _logger.LogWarning("Plugin '{PluginId}' is enabled but its manifest was not found (may have been removed). Skipping.", pluginId);
                        continue;
                    }

                    var load = _externalHost.LoadRuntime(pluginId);
                    externalLoads.Add(load);
                    plugins.Add(pluginId, load.Plugin);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to load plugin '{PluginId}'. Skipping.", pluginId);
                }
            }

            var services = new ServiceCollection();
            services.AddSingleton(_rootServices.GetRequiredService<IConfiguration>());
            services.AddSingleton(_rootServices.GetRequiredService<IHostEnvironment>());
            // Forward the root logging factory so plugins can inject ILogger<T> and their log
            // entries flow through the host's Serilog pipeline. ValidateOnBuild below fails
            // otherwise, since plugins like RedisLuaRateLimitStore take ILogger<T> in their ctor.
            services.AddSingleton(_rootServices.GetRequiredService<ILoggerFactory>());
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            foreach (var plugin in plugins.Values)
            {
                services.AddSingleton(typeof(IGatewayPlugin), plugin);
                plugin.ConfigureServices(services);
            }

            var pluginServices = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            var servicesWithFallback = new PluginRuntimeServiceProvider(pluginServices, _rootServices);
            var middleware = BuildMiddlewarePipeline(servicesWithFallback, plugins);
            var proxyPipeline = BuildProxyPipeline(servicesWithFallback, plugins);

            var dashboardBuilder = new PluginDashboardBuilder();
            foreach (var plugin in plugins.Values)
                plugin.ConfigureDashboard(dashboardBuilder);

            var candidate = new PluginRuntimeDomain(servicesWithFallback, plugins, middleware, proxyPipeline, externalLoads, dashboardBuilder);
            return new PluginRuntimeDomainPreparation(this, candidate);
        }
        catch
        {
            foreach (var load in externalLoads.AsEnumerable().Reverse())
                await load.DisposeAsync();
            throw;
        }
    }

    public async Task<PluginRuntimeTransitionResult> TransitionAsync(
        IReadOnlyCollection<string> enabledPluginIds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var preparation = await PrepareAsync(enabledPluginIds, cancellationToken);
            var health = await preparation.CheckHealthAsync(cancellationToken);
            if (health.Status == PluginHealthStatus.Unhealthy)
                return new PluginRuntimeTransitionResult(false, health.Message ?? "Plugin runtime health check failed.");

            await preparation.CommitAsync(cancellationToken);
            return new PluginRuntimeTransitionResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to transition the plugin runtime domain. The previous domain remains active.");
            return new PluginRuntimeTransitionResult(false, ex.Message);
        }
    }

    internal Task CommitAsync(PluginRuntimeDomain candidate)
    {
        var previous = Interlocked.Exchange(ref _current, candidate);
        _ = RetirePreviousAsync(previous);
        StartPostTransitionWatch();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => Current.RetireAndDisposeAsync();

    private async Task RetirePreviousAsync(PluginRuntimeDomain previous)
    {
        try
        {
            await previous.RetireAndDisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispose a retired plugin runtime domain.");
        }
    }

    /// <summary>
    /// Watches the proxy pipeline error rate for a short window after a runtime domain transition.
    /// The watch only logs an alert when the error rate spikes; it never rolls back automatically.
    /// </summary>
    private void StartPostTransitionWatch()
    {
        var monitor = _rootServices.GetService<IPluginResourceMonitor>();
        if (monitor == null)
            return;

        if (Interlocked.CompareExchange(ref _postTransitionWatchRunning, 1, 0) != 0)
            return;

        var before = TrySnapshot(monitor);
        if (before == null)
        {
            Volatile.Write(ref _postTransitionWatchRunning, 0);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(PostTransitionWatchWindow).ConfigureAwait(false);
                var after = TrySnapshot(monitor);
                if (after == null)
                    return;

                var requestDelta = after.TotalRequests - before.TotalRequests;
                var errorDelta = after.TotalErrors - before.TotalErrors;
                if (requestDelta < PostTransitionWatchMinimumRequests || errorDelta <= 0)
                    return;

                var errorRate = (double)errorDelta / requestDelta;
                if (errorRate < PostTransitionWatchErrorRateThreshold)
                    return;

                var offenders = after.PerPlugin
                    .Select(current =>
                    {
                        var priorErrors = before.PerPlugin
                            .Where(p => string.Equals(p.PluginId, current.PluginId, StringComparison.OrdinalIgnoreCase))
                            .Select(p => (long?)p.Errors)
                            .FirstOrDefault();
                        return (current.PluginId, ErrorDelta: current.Errors - (priorErrors ?? 0));
                    })
                    .Where(p => p.ErrorDelta > 0)
                    .OrderByDescending(p => p.ErrorDelta)
                    .Take(3)
                    .Select(p => $"{p.PluginId} (+{p.ErrorDelta})")
                    .ToArray();

                _logger.LogWarning(
                    "Plugin runtime domain transitioned {WindowSeconds}s ago and the proxy pipeline error rate spiked to {ErrorRate:P1} ({ErrorCount} of {RequestCount} requests). Top error sources: {Offenders}. No automatic rollback is performed; verify the recently toggled plugins.",
                    PostTransitionWatchWindow.TotalSeconds, errorRate, errorDelta, requestDelta,
                    offenders.Length > 0 ? string.Join(", ", offenders) : "unknown");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Post-transition plugin pipeline watch aborted.");
            }
            finally
            {
                Volatile.Write(ref _postTransitionWatchRunning, 0);
            }
        });
    }

    private PluginPipelineSnapshot? TrySnapshot(IPluginResourceMonitor monitor)
    {
        try
        {
            var usage = monitor.GetAllUsage();
            long totalRequests = 0;
            long totalErrors = 0;
            var perPlugin = new List<PluginPipelinePluginStats>(usage.Count);
            foreach (var entry in usage)
            {
                totalRequests += entry.RequestCount;
                totalErrors += entry.ErrorCount;
                perPlugin.Add(new(entry.PluginId, entry.RequestCount, entry.ErrorCount));
            }
            return new PluginPipelineSnapshot(totalRequests, totalErrors, perPlugin);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to snapshot plugin request statistics for the post-transition watch.");
            return null;
        }
    }

    private sealed record PluginPipelinePluginStats(string PluginId, long Requests, long Errors);

    private sealed record PluginPipelineSnapshot(
        long TotalRequests,
        long TotalErrors,
        IReadOnlyList<PluginPipelinePluginStats> PerPlugin);

    private async Task InvokeCurrentPipelineAsync(
        HttpContext context,
        Func<PluginRuntimeDomain, RequestDelegate> pipelineSelector,
        RequestDelegate next)
    {
        PluginRuntimeDomain domain;
        PluginRuntimeDomainLease lease;
        do
        {
            domain = Current;
        }
        while (!domain.TryAcquire(out lease));

        using (lease)
        {
            await InvokePipelineAsync(context, pipelineSelector(domain), next);
        }
    }

    private static async Task InvokePipelineAsync(HttpContext context, RequestDelegate pipeline, RequestDelegate next)
    {
        context.Items[PipelineContinuationKey] = next;
        try
        {
            await pipeline(context);
        }
        finally
        {
            context.Items.Remove(PipelineContinuationKey);
        }
    }

    private static RequestDelegate BuildProxyPipeline(
        IServiceProvider services,
        IReadOnlyDictionary<string, IGatewayPlugin> plugins)
    {
        var builder = new ReverseProxyApplicationBuilderAdapter(new ApplicationBuilder(services));

        // Track per-plugin request metrics: latency, success/failure, uptime start
        builder.Use(async (context, next) =>
        {
            var sw = Stopwatch.StartNew();
            bool succeeded = true;
            try
            {
                await next(context);
            }
            catch
            {
                succeeded = false;
                throw;
            }
            finally
            {
                sw.Stop();
                try
                {
                    var monitor = context.RequestServices.GetService<IPluginResourceMonitor>();
                    var pluginManager = context.RequestServices.GetService<IGatewayPluginManager>();
                    if (monitor != null)
                    {
                        var enabledIds = pluginManager?.GetPluginStates()
                            .Where(s => s.Enabled)
                            .Select(s => s.Manifest.Id)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        foreach (var plugin in plugins.Values)
                        {
                            if (enabledIds?.Contains(plugin.PluginId) == true)
                                monitor.RecordRequest(plugin.PluginId, sw.ElapsedMilliseconds, succeeded);
                        }
                    }
                }
                catch { /* best-effort monitoring */ }
            }
        });

        foreach (var pluginId in ProxyPluginOrder)
            if (plugins.TryGetValue(pluginId, out var plugin))
                plugin.ConfigureProxyPipeline(builder);
        builder.Run(ContinueHostPipelineAsync);
        return builder.Build();
    }

    private static RequestDelegate BuildMiddlewarePipeline(
        IServiceProvider services,
        IReadOnlyDictionary<string, IGatewayPlugin> plugins)
    {
        var builder = new ApplicationBuilder(services);
        if (plugins.TryGetValue("waf", out var waf))
            waf.ConfigureMiddleware(builder);
        builder.Run(ContinueHostPipelineAsync);
        return builder.Build();
    }



    private static Task ContinueHostPipelineAsync(HttpContext context) =>
        context.Items.TryGetValue(PipelineContinuationKey, out var continuation) && continuation is RequestDelegate next
            ? next(context)
            : Task.CompletedTask;

    private sealed class ReverseProxyApplicationBuilderAdapter(IApplicationBuilder inner) : IReverseProxyApplicationBuilder
    {
        public IServiceProvider ApplicationServices { get => inner.ApplicationServices; set => inner.ApplicationServices = value; }
        public IFeatureCollection ServerFeatures => inner.ServerFeatures;
        public IDictionary<string, object?> Properties => inner.Properties;
        public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware) { inner.Use(middleware); return this; }
        public IApplicationBuilder New() => new ReverseProxyApplicationBuilderAdapter(inner.New());
        public RequestDelegate Build() => inner.Build();
    }

    private sealed class PluginRuntimeServiceProvider(IServiceProvider plugin, IServiceProvider root) :
        IServiceProvider, IServiceScopeFactory, IDisposable, IAsyncDisposable
    {
        public object? GetService(Type serviceType) => plugin.GetService(serviceType) ?? root.GetService(serviceType);
        public IServiceScope CreateScope() => plugin.GetRequiredService<IServiceScopeFactory>().CreateScope();
        public void Dispose() => (plugin as IDisposable)?.Dispose();
        public ValueTask DisposeAsync() => plugin is IAsyncDisposable asyncDisposable
            ? asyncDisposable.DisposeAsync()
            : DisposeSynchronously();
        private ValueTask DisposeSynchronously() { Dispose(); return ValueTask.CompletedTask; }
    }
}
