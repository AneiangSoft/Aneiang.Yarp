using System.Collections.Immutable;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Aneiang.Yarp.Dashboard.Extensions;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Modules.AI.Tools;
using Aneiang.Yarp.Dashboard.Modules.Plugins;
using Aneiang.Yarp.Dashboard.Modules.Plugins.Runtime;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Infrastructure;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Aneiang.Yarp.Storage.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

var failures = new List<string>();

Run("Removed compatibility contracts stay isolated from runtime source", () =>
{
    var root = FindRepositoryRoot();
    var sourceRoot = Path.Combine(root, "src");
    var migrationSegment = $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}";
    var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Contains(migrationSegment, StringComparison.OrdinalIgnoreCase))
        .ToArray();
    var forbidden = new[]
    {
        "RetryOptions",
        "RateLimitOptions",
        "Gateway:Dashboard:Plugins",
        "EnableRateLimiting",
        "EnablePassiveHealthCheck",
        "DefaultHealthCheckService"
    };

    foreach (var path in sourceFiles)
    {
        var source = File.ReadAllText(path);
        foreach (var symbol in forbidden)
            False(source.Contains(symbol, StringComparison.Ordinal));
    }
});

await RunAsync("Distributed rate limit backends preserve atomic window counts", async () =>
{
    var memory = new MemoryDistributedRateLimitBackend();
    var expiry = DateTimeOffset.UtcNow.AddMinutes(1);
    var memoryCounts = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => memory.IncrementAsync("memory", expiry, default).AsTask()));
    Equal(100, memoryCounts.Max());

    var path = Path.Combine(Path.GetTempPath(), $"aneiang-rate-limit-{Guid.NewGuid():N}.db");
    Environment.SetEnvironmentVariable("ANEIANG_RATE_LIMIT_SQLITE", $"Data Source={path};Mode=ReadWriteCreate;Cache=Shared;Pooling=False");
    try
    {
        var sqlite = new SqliteDistributedRateLimitBackend();
        var sqliteCounts = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => sqlite.IncrementAsync("sqlite", expiry, default).AsTask()));
        Equal(40, sqliteCounts.Max());
        Equal(40, sqliteCounts.Distinct().Count());
    }
    finally
    {
        Environment.SetEnvironmentVariable("ANEIANG_RATE_LIMIT_SQLITE", null);
        if (File.Exists(path)) File.Delete(path);
    }
});

Run("Infrastructure plugin schemas expose shared backends and discovery adapters", () =>
{
    var rateSchema = JsonDocument.Parse(new DistributedRateLimitPlugin().Manifest.Schemas.Single().ConfigJsonSchema);
    True(rateSchema.RootElement.GetProperty("properties").GetProperty("backend").GetProperty("enum").EnumerateArray().Any(x => x.GetString() == "Sqlite"));
    var discoverySchema = JsonDocument.Parse(new HttpServiceDiscoveryPlugin().Manifest.Schemas.Single().ConfigJsonSchema);
    var modes = discoverySchema.RootElement.GetProperty("properties").GetProperty("mode").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToHashSet();
    True(new[] { "Consul", "Nacos", "Eureka", "Kubernetes" }.All(modes.Contains));
});

Run("Semantic version constraints support AND ranges and SemVer prerelease precedence", () =>
{
    True(SatisfiesVersionConstraint("2.5.0", ">=2.0.0 <3.0.0"));
    True(!SatisfiesVersionConstraint("3.0.0", ">=2.0.0 <3.0.0"));
    True(SatisfiesVersionConstraint("2.0.0-rc.10", ">2.0.0-rc.2 <2.0.0"));
    True(!SatisfiesVersionConstraint("2.0.0-alpha", ">=2.0.0"));
    True(SatisfiesVersionConstraint("1.0.0-alpha.1", ">1.0.0-alpha <1.0.0-alpha.beta"));
    True(!SatisfiesVersionConstraint("1.0.0-Alpha", "=1.0.0-alpha"));
    True(!TrySatisfyVersionConstraint("2.5.0", ">=2.0.0 invalid", out var error));
    True(error?.Contains("invalid", StringComparison.OrdinalIgnoreCase) == true);
});

Run("External plugin registration exposes verifiable unload state", () =>
{
    True(Enum.IsDefined(ExternalPluginRegistrationStatus.UnloadPending));
    True(Enum.IsDefined(ExternalPluginRegistrationStatus.Unloaded));
    var registration = new ExternalPluginRegistration(
        new PluginManifest("external", "External", "1.0.0", [], [], 0, new(), [], "test"),
        "plugin.json",
        ExternalPluginRegistrationStatus.Unloaded,
        IsCollectible: true,
        IsLoadContextAlive: false);
    Equal(ExternalPluginRegistrationStatus.Unloaded, registration.Status);
    Equal(false, registration.IsLoadContextAlive);
});

await RunAsync("Plugin runtime domain resolves plugin DI and preserves the old domain on failed health", async () =>
{
    var healthy = new RuntimeDomainTestPlugin("runtime-healthy", healthy: true);
    var unhealthy = new RuntimeDomainTestPlugin("runtime-unhealthy", healthy: false);
    var root = new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
        .AddSingleton<IHostEnvironment>(new TestHostEnvironment(Directory.GetCurrentDirectory()))
        .AddSingleton<IGatewayPlugin>(healthy)
        .AddSingleton<IGatewayPlugin>(unhealthy)
        .BuildServiceProvider();
    var host = new ExternalGatewayPluginHost(
        root.GetRequiredService<IHostEnvironment>(),
        root.GetRequiredService<IConfiguration>(),
        NullLogger<ExternalGatewayPluginHost>.Instance);
    await using var manager = new PluginRuntimeDomainManager(
        root,
        host,
        NullLogger<PluginRuntimeDomainManager>.Instance);

    var first = await manager.TransitionAsync([healthy.PluginId]);
    True(first.Succeeded);
    True(manager.Current.Services.GetService<RuntimeDomainMarker>() != null);
    var oldDomain = manager.Current;

    var failed = await manager.TransitionAsync([unhealthy.PluginId]);
    False(failed.Succeeded);
    True(ReferenceEquals(oldDomain, manager.Current));

    var nativeTransition = await manager.TransitionAsync([healthy.PluginId, NativePluginAdapters.RouteCors]);
    True(nativeTransition.Succeeded);
    True(manager.Current.Plugins.ContainsKey(healthy.PluginId));
    False(manager.Current.Plugins.ContainsKey(NativePluginAdapters.RouteCors));
});

await RunAsync("Plugin runtime domain drains in-flight requests before disposal", async () =>
{
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var draining = new RuntimeDomainDrainPlugin(entered, release, disposed);
    var root = new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
        .AddSingleton<IHostEnvironment>(new TestHostEnvironment(Directory.GetCurrentDirectory()))
        .AddSingleton<IGatewayPlugin>(draining)
        .BuildServiceProvider();
    var host = new ExternalGatewayPluginHost(
        root.GetRequiredService<IHostEnvironment>(),
        root.GetRequiredService<IConfiguration>(),
        NullLogger<ExternalGatewayPluginHost>.Instance);
    await using var manager = new PluginRuntimeDomainManager(
        root,
        host,
        NullLogger<PluginRuntimeDomainManager>.Instance);

    True((await manager.TransitionAsync([draining.PluginId])).Succeeded);
    var request = manager.InvokeMiddlewareAsync(new DefaultHttpContext(), _ => Task.CompletedTask);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    True((await manager.TransitionAsync([])).Succeeded);
    False(disposed.Task.IsCompleted);
    release.TrySetResult();
    await request.WaitAsync(TimeSpan.FromSeconds(5));
    if (await Task.WhenAny(disposed.Task, Task.Delay(TimeSpan.FromSeconds(5))) != disposed.Task)
        throw new InvalidOperationException("The retired runtime provider was not disposed after the in-flight request drained.");
});

Run("Plugin manifest dependencies are exposed and built-in adapters are protected", () =>
{
    var manifest = new Aneiang.Yarp.Dashboard.Infrastructure.Plugin.PluginManifest(
        "dependent", "Dependent", "1.0", [], [], 0, new(), [], "test")
    {
        Dependencies = [new("base")]
    };
    Equal("base", manifest.Dependencies.Single().PluginId);
    True(!new Aneiang.Yarp.Dashboard.Infrastructure.Plugin.PluginStateChangeResult(false, "blocked").Succeeded);
});

Run("ClientIpResolver ignores spoofed forwarding headers", () =>
{
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.8");
    context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
    context.Request.Headers["X-Real-IP"] = "203.0.113.11";
    Equal("10.0.0.8", ClientIpResolver.GetClientIp(context));
});

await RunAsync("Dashboard API key is header-only", async () =>
{
    var options = Options.Create(new DashboardOptions { AuthMode = DashboardAuthMode.ApiKey, ApiKey = "secret", ApiKeyHeaderName = "X-Api-Key" });
    var service = new DashboardAuthorizationService(options, NullLogger<DashboardAuthorizationService>.Instance);
    var queryContext = new DefaultHttpContext();
    queryContext.Request.QueryString = new QueryString("?api-key=secret");
    False(await service.IsAuthorizedAsync(queryContext));
    var headerContext = new DefaultHttpContext();
    headerContext.Request.Headers["X-Api-Key"] = "secret";
    True(await service.IsAuthorizedAsync(headerContext));
});

await RunAsync("JWT query token is restricted to dashboard hubs", async () =>
{
    const string secret = "regression-test-secret-that-is-long-enough";
    var token = DashboardJwtHelper.GenerateToken("tester", secret);
    var service = new DashboardAuthorizationService(Options.Create(new DashboardOptions { AuthMode = DashboardAuthMode.CustomJwt, JwtSecret = secret }), NullLogger<DashboardAuthorizationService>.Instance);
    var apiContext = new DefaultHttpContext();
    apiContext.Request.Path = "/apigateway/api/routes";
    apiContext.Request.QueryString = new QueryString($"?access_token={token}");
    False(await service.IsAuthorizedAsync(apiContext));
    var hubContext = new DefaultHttpContext();
    hubContext.Request.Path = "/apigateway/hubs/overview";
    hubContext.Request.QueryString = new QueryString($"?access_token={token}");
    True(await service.IsAuthorizedAsync(hubContext));
});

Run("Response capture does not depend on request content type", () =>
{
    var context = new DefaultHttpContext();
    context.Request.ContentType = "application/octet-stream";
    True(ProxyLogBodyReader.IsResponseBodyCaptureCandidate(context.Request));
    context.Request.Headers.Range = "bytes=0-99";
    False(ProxyLogBodyReader.IsResponseBodyCaptureCandidate(context.Request));
});

Run("Proxy log runtime settings use dashboard defaults", () =>
{
    var runtime = new ProxyLogRuntimeSettings(Options.Create(new DashboardOptions
    {
        LogPersistenceEnabled = true,
        LogMetaRetentionDays = 7,
        LogBodyRetentionDays = 2,
        EnableProxyRequestBodyCapture = true,
        EnableProxyResponseBodyCapture = false,
        LogMaxBodyBufferBytes = 16384,
        EnableLogSampling = true,
        LogSamplingRate = 0.25,
        LogErrorsOnly = true,
        MinLogLevel = "Warning"
    }));
    var snapshot = runtime.Current;
    True(snapshot.PersistenceEnabled);
    Equal(7, snapshot.MetaRetentionDays);
    Equal(2, snapshot.BodyRetentionDays);
    True(snapshot.RequestBodyCaptureEnabled);
    False(snapshot.ResponseBodyCaptureEnabled);
    Equal(16384, snapshot.MaxBodyBufferBytes);
    Equal(0.25, snapshot.SamplingRate);
    Equal(2, snapshot.MinLogLevelNumeric);
});

await RunAsync("Destination persistence mapping is lossless", () =>
{
    var original = new KeyValuePair<string, DestinationConfig>("primary", new DestinationConfig
    {
        Address = "https://backend.example/", Host = "backend.internal", Health = "https://health.example/ready",
        Metadata = new Dictionary<string, string> { ["zone"] = "east", ["weight"] = "10" }
    });
    var restored = new[] { original.ToEntity("cluster-a") }.ToDestinations()["primary"];
    Equal(original.Value.Address, restored.Address);
    Equal(original.Value.Host, restored.Host);
    Equal(original.Value.Health, restored.Health);
    Equal("east", restored.Metadata!["zone"]);
    Equal("10", restored.Metadata["weight"]);
    return Task.CompletedTask;
});

await RunAsync("Cluster rename preserves native extensions", () =>
{
    var original = new ClusterConfig
    {
        ClusterId = "old-cluster",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["primary"] = new DestinationConfig
            {
                Address = "https://backend.example/", Host = "backend.internal", Health = "https://health.example/ready",
                Metadata = new Dictionary<string, string> { ["zone"] = "east", ["weight"] = "10" }
            }
        },
        SessionAffinity = new SessionAffinityConfig { Enabled = true, Policy = "Cookie", AffinityKeyName = "affinity" },
        HealthCheck = new Yarp.ReverseProxy.Configuration.HealthCheckConfig
        {
            AvailableDestinationsPolicy = "HealthyOrPanic",
            Passive = new Yarp.ReverseProxy.Configuration.PassiveHealthCheckConfig { Enabled = true, Policy = "TransportFailureRate" },
            Active = new Yarp.ReverseProxy.Configuration.ActiveHealthCheckConfig { Enabled = true, Path = "/ready", Interval = TimeSpan.FromSeconds(7) }
        },
        HttpClient = new HttpClientConfig { MaxConnectionsPerServer = 23 },
        HttpRequest = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(19) },
        Metadata = new Dictionary<string, string> { ["owner"] = "platform" }
    };
    var renamed = original with { ClusterId = "new-cluster" };
    Equal("new-cluster", renamed.ClusterId);
    Equal(original.Destinations!["primary"].Host, renamed.Destinations!["primary"].Host);
    Equal(original.Destinations["primary"].Health, renamed.Destinations["primary"].Health);
    Equal("east", renamed.Destinations["primary"].Metadata!["zone"]);
    Equal("10", renamed.Destinations["primary"].Metadata!["weight"]);
    Equal(true, renamed.SessionAffinity!.Enabled);
    Equal("Cookie", renamed.SessionAffinity.Policy);
    Equal("affinity", renamed.SessionAffinity.AffinityKeyName);
    Equal("HealthyOrPanic", renamed.HealthCheck!.AvailableDestinationsPolicy);
    Equal(true, renamed.HealthCheck.Passive!.Enabled);
    Equal("TransportFailureRate", renamed.HealthCheck.Passive.Policy);
    Equal(true, renamed.HealthCheck.Active!.Enabled);
    Equal("/ready", renamed.HealthCheck.Active.Path);
    Equal(TimeSpan.FromSeconds(7), renamed.HealthCheck.Active.Interval);
    Equal(23, renamed.HttpClient!.MaxConnectionsPerServer);
    Equal(TimeSpan.FromSeconds(19), renamed.HttpRequest!.ActivityTimeout);
    Equal("platform", renamed.Metadata!["owner"]);
    return Task.CompletedTask;
});

await RunAsync("PluginBinding SQLite CRUD and uniqueness", TestPluginBindingRepositoryAsync);
await RunAsync("GatewayPluginManager persists state in gateway_plugins and imports legacy JSON", TestGatewayPluginStatePersistenceAsync);
await RunAsync("GatewaySnapshotCompiler filters disabled and missing targets", TestSnapshotCompilerAsync);
await RunAsync("GatewaySnapshotCompiler keeps UID bindings stable across target renames", TestSnapshotUidRenameStabilityAsync);
Run("GatewaySnapshotPublisher rejects stale versions", TestSnapshotPublisher);
Run("Plugin execution plan is reused and invalidated by snapshot publication", TestPluginExecutionPlanCache);
Run("Service discovery manifest and execution plan share the canonical plugin id", TestServiceDiscoveryPluginId);
await RunAsync("Service discovery refreshes static destinations only while its resource runs", TestServiceDiscoveryStaticRefreshAsync);
await RunAsync("Plugin health and runtime resource contracts expose accurate state", TestPluginHealthAndResourceContractsAsync);
Run("Native adapters apply and validate configuration", TestNativeAdapters);
Run("Plugin schema validates nested composition", TestPluginSchemaValidation);
Run("Legacy global policy chains stay isolated from dashboard activation", TestLegacyGlobalPolicyIsolation);

if (failures.Count > 0)
{
    Console.Error.WriteLine($"Regression checks failed: {failures.Count}");
    foreach (var failure in failures) Console.Error.WriteLine($"- {failure}");
    return 1;
}
Console.WriteLine("All regression checks passed.");
return 0;

async Task TestPluginBindingRepositoryAsync()
{
    var dbPath = Path.Combine(Path.GetTempPath(), $"aneiang-yarp-regression-{Guid.NewGuid():N}.db");
    try
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var options = Options.Create(new StorageOptions { Sqlite = new SqliteStorageOptions { ConnectionString = $"Data Source={dbPath}" } });
        var repository = new SqlitePluginConfigurationRepository(new SqliteConnectionFactory(options, services));
        var first = NewBinding("binding-1", PluginBindingScope.Route, "route-a", "timeout", true, "{\"timeout\":\"00:00:05\"}");
        await repository.UpsertBindingAsync(first);
        var loaded = await repository.GetBindingAsync(first.Id);
        True(loaded is not null && loaded.PluginId == "timeout" && loaded.Enabled);
        await repository.UpsertBindingAsync(NewBinding(first.Id, first.Scope, first.ScopeId, first.PluginId, false, "{\"timeout\":\"00:00:10\"}"));
        loaded = await repository.GetBindingAsync(first.Id);
        True(loaded is not null && !loaded.Enabled && loaded.ConfigJson.Contains("10"));
        await ThrowsAsync<Exception>(() => repository.UpsertBindingAsync(NewBinding("binding-2", first.Scope, first.ScopeId, first.PluginId, true, "{}")));
        Equal(1, (await repository.GetBindingsAsync(PluginBindingScope.Route, "route-a")).Count);
        True(await repository.DeleteBindingAsync(first.Id));
        Equal(0, (await repository.GetBindingsAsync(PluginBindingScope.Route, "route-a")).Count);
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" }) if (File.Exists(path)) File.Delete(path);
    }
}

async Task TestGatewayPluginStatePersistenceAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"aneiang-yarp-plugin-state-{Guid.NewGuid():N}");
    var dbPath = Path.Combine(root, "state.db");
    Directory.CreateDirectory(root);
    try
    {
        await File.WriteAllTextAsync(Path.Combine(root, "plugin-states.json"), "{\"test-plugin\":false}");
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var options = Options.Create(new StorageOptions { Sqlite = new SqliteStorageOptions { ConnectionString = $"Data Source={dbPath}" } });
        var repository = new SqliteGatewayPluginRepository(new SqliteConnectionFactory(options, services));
        var environment = new TestHostEnvironment(root);
        var configuration = new ConfigurationBuilder().Build();
        var externalHost = new ExternalGatewayPluginHost(environment, configuration, NullLogger<ExternalGatewayPluginHost>.Instance);
        var publisher = new GatewaySnapshotPublisher();
        var plugin = new TestGatewayPlugin();

        var imported = new GatewayPluginManager([plugin], configuration, environment,
            NullLogger<GatewayPluginManager>.Instance, publisher, externalHost, repository);
        False(imported.IsPluginEnabled(plugin.PluginId));
        var importedRow = await repository.GetAsync(plugin.PluginId);
        True(importedRow is not null);
        Equal("1.2.3", importedRow!.Version);
        Equal(false, importedRow.Enabled);
        Equal(true, importedRow.IsBuiltIn);
        Equal("Loaded", importedRow.RegistrationStatus);
        True(importedRow.InstalledAt <= importedRow.UpdatedAt);
        False(File.Exists(Path.Combine(root, "plugin-states.json")));

        True(imported.SetPluginEnabled(plugin.PluginId, true).Succeeded);
        var enabledRow = await repository.GetAsync(plugin.PluginId);
        Equal(true, enabledRow!.Enabled);
        var installedAt = enabledRow.InstalledAt;

        await File.WriteAllTextAsync(Path.Combine(root, "plugin-states.json"), "{\"test-plugin\":false}");
        var reloaded = new GatewayPluginManager([plugin], configuration, environment,
            NullLogger<GatewayPluginManager>.Instance, publisher, externalHost, repository);
        True(reloaded.IsPluginEnabled(plugin.PluginId));
        var reloadedRow = await repository.GetAsync(plugin.PluginId);
        Equal(installedAt, reloadedRow!.InstalledAt);
        True(reloadedRow.UpdatedAt >= enabledRow.UpdatedAt);
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

async Task TestSnapshotCompilerAsync()
{
    var repository = new InMemoryPluginRepository(
    [
        NewBinding("route-on", PluginBindingScope.Route, "route-a", NativePluginAdapters.RouteTimeout, true, "{\"Timeout\":\"00:00:05\"}"),
        NewBinding("route-cors", PluginBindingScope.Route, "route-a", NativePluginAdapters.RouteCors, true, "{\"CorsPolicy\":\"route-cors-policy\"}"),
        NewBinding("route-rate-limit", PluginBindingScope.Route, "route-a", NativePluginAdapters.RouteRateLimit, true, "{\"RateLimiterPolicy\":\"route-rate-policy\"}"),
        NewBinding("route-compression", PluginBindingScope.Route, "route-a", NativePluginAdapters.RouteCompression, true, "{\"Enabled\":false}"),
        NewBinding("route-pipeline", PluginBindingScope.Route, "route-a", OrderedRouteCompiler.PluginId, true, "{\"value\":17}"),
        NewBinding("route-disabled", PluginBindingScope.Route, "route-a", NativePluginAdapters.RouteTimeout, false, "{\"Timeout\":\"00:00:10\"}"),
        NewBinding("route-missing", PluginBindingScope.Route, "missing-route", NativePluginAdapters.RouteTimeout, true, "{\"Timeout\":\"00:00:05\"}"),
        NewBinding("cluster-on", PluginBindingScope.Cluster, "cluster-a", NativePluginAdapters.ClusterLoadBalancing, true, "{\"LoadBalancingPolicy\":\"RoundRobin\"}"),
        NewBinding("cluster-pipeline", PluginBindingScope.Cluster, "cluster-a", OrderedClusterCompiler.PluginId, true, "{\"value\":23}"),
        NewBinding("cluster-missing", PluginBindingScope.Cluster, "missing-cluster", NativePluginAdapters.ClusterLoadBalancing, true, "{\"LoadBalancingPolicy\":\"Random\"}")
    ]);
    var activation = new ToggleActivationState();
    var nativeAdapters = new NativePluginAdapters();
    var compiler = new GatewaySnapshotCompiler(
        repository,
        activation,
        [nativeAdapters, new OrderedRouteCompiler(20, "second"), new OrderedRouteCompiler(10, "first")],
        [nativeAdapters, new OrderedClusterCompiler(20, "second"), new OrderedClusterCompiler(10, "first")]);
    var routes = new[] { new RouteConfig { RouteId = "route-a", ClusterId = "cluster-a", Match = new RouteMatch { Path = "/{**catch-all}" } } };
    var clusters = new[] { new ClusterConfig { ClusterId = "cluster-a", Destinations = new Dictionary<string, DestinationConfig>() } };
    var snapshot = await compiler.CompileAsync(routes, clusters, 7);
    Equal(7L, snapshot.Version);
    True(snapshot.RoutePlugins["route-a"].Select(x => x.BindingId).ToHashSet(StringComparer.Ordinal).SetEquals([
        "route-on", "route-cors", "route-rate-limit", "route-compression", "route-pipeline"]));
    True(snapshot.ClusterPlugins["cluster-a"].Select(x => x.BindingId).SequenceEqual(["cluster-on", "cluster-pipeline"]));
    False(snapshot.RoutePlugins.ContainsKey("missing-route"));
    False(snapshot.ClusterPlugins.ContainsKey("missing-cluster"));
    Equal(TimeSpan.FromSeconds(5), snapshot.Routes[0].Timeout);
    Equal("route-cors-policy", snapshot.Routes[0].CorsPolicy);
    Equal("route-rate-policy", snapshot.Routes[0].RateLimiterPolicy);
    True(snapshot.Routes[0].Transforms!.Any(transform =>
        transform.TryGetValue("RequestHeaderRemove", out var header) && header == "Accept-Encoding"));
    Equal("RoundRobin", snapshot.Clusters[0].LoadBalancingPolicy);
    True(snapshot.RouteExecutionPlans["route-a"].Plugins.Where(x => x.Binding.PluginId == OrderedRouteCompiler.PluginId).Select(x => x.GetRuntimeConfig<OrderedConfig>().Name).SequenceEqual(["first", "second"]));
    True(snapshot.ClusterExecutionPlans["cluster-a"].Plugins.Where(x => x.Binding.PluginId == OrderedClusterCompiler.PluginId).Select(x => x.GetRuntimeConfig<OrderedConfig>().Name).SequenceEqual(["first", "second"]));
    Equal(17, snapshot.RouteExecutionPlans["route-a"].Plugins.First(x => x.Binding.PluginId == OrderedRouteCompiler.PluginId).GetRuntimeConfig<OrderedConfig>().Value);
    Equal(23, snapshot.ClusterExecutionPlans["cluster-a"].Plugins.First(x => x.Binding.PluginId == OrderedClusterCompiler.PluginId).GetRuntimeConfig<OrderedConfig>().Value);
    True(snapshot.RoutePlugins["route-a"].Any(x => x.PluginId == NativePluginAdapters.RouteTimeout && x.BindingId == "route-on"));
    activation.Enabled = false;
    var disabledSnapshot = await compiler.CompileAsync(routes, clusters, 8);
    False(disabledSnapshot.RoutePlugins.ContainsKey("route-a"));
    False(disabledSnapshot.ClusterPlugins.ContainsKey("cluster-a"));
    activation.Enabled = true;
    var restoredSnapshot = await compiler.CompileAsync(routes, clusters, 9);
    True(restoredSnapshot.RoutePlugins["route-a"].Any(x => x.BindingId == "route-on"));
    True(restoredSnapshot.ClusterPlugins["cluster-a"].Any(x => x.BindingId == "cluster-on"));
}

async Task TestSnapshotUidRenameStabilityAsync()
{
    var routeBinding = NewBinding("route-uid-binding", PluginBindingScope.Route, "old-route", NativePluginAdapters.RouteTimeout, true, "{\"Timeout\":\"00:00:05\"}");
    routeBinding.RouteUid = "route-uid-1";
    var clusterBinding = NewBinding("cluster-uid-binding", PluginBindingScope.Cluster, "old-cluster", NativePluginAdapters.ClusterLoadBalancing, true, "{\"LoadBalancingPolicy\":\"RoundRobin\"}");
    clusterBinding.ClusterUid = "cluster-uid-1";
    var repository = new InMemoryPluginRepository([routeBinding, clusterBinding]);
    var routeRepository = new InMemoryRouteRepository([new RouteEntity { RouteUid = "route-uid-1", RouteId = "renamed-route", ClusterId = "renamed-cluster" }]);
    var clusterRepository = new InMemoryClusterRepository([new ClusterEntity { ClusterUid = "cluster-uid-1", ClusterId = "renamed-cluster" }]);
    var compiler = new GatewaySnapshotCompiler(repository, new ToggleActivationState(), routes: routeRepository, clusters: clusterRepository);
    var snapshot = await compiler.CompileAsync(
        [new RouteConfig { RouteId = "renamed-route", ClusterId = "renamed-cluster", Match = new RouteMatch { Path = "/{**catch-all}" } }],
        [new ClusterConfig { ClusterId = "renamed-cluster", Destinations = new Dictionary<string, DestinationConfig>() }], 10);

    Equal("route-uid-binding", snapshot.RoutePlugins["renamed-route"].Single().BindingId);
    Equal("cluster-uid-binding", snapshot.ClusterPlugins["renamed-cluster"].Single().BindingId);
    False(snapshot.RoutePlugins.ContainsKey("old-route"));
    False(snapshot.ClusterPlugins.ContainsKey("old-cluster"));
}

void TestSnapshotPublisher()
{
    var publisher = new GatewaySnapshotPublisher();
    publisher.Publish(EmptySnapshot(1));
    publisher.Publish(EmptySnapshot(2));
    Throws<InvalidOperationException>(() => publisher.Publish(EmptySnapshot(2)));
    Throws<InvalidOperationException>(() => publisher.Publish(EmptySnapshot(1)));
    Equal(2L, publisher.Current.Version);
}

void TestPluginExecutionPlanCache()
{
    var publisher = new GatewaySnapshotPublisher();
    publisher.Publish(SnapshotWithRouteBinding(1, "{\"enabled\":true,\"maxRetries\":1,\"retryOnStatusCodes\":[503]}"));
    var provider = new GatewayPluginExecutionPlanProvider(publisher, NullLogger<GatewayPluginExecutionPlanProvider>.Instance);

    var first = provider.Current;
    var reused = provider.Current;
    True(ReferenceEquals(first, reused));
    Equal(1, first.RetryByRoute["route-a"].MaxRetries);

    publisher.Publish(SnapshotWithRouteBinding(2, "{\"enabled\":true,\"maxRetries\":3,\"retryOnStatusCodes\":[504]}"));
    var refreshed = provider.Current;
    False(ReferenceEquals(first, refreshed));
    Equal(2L, refreshed.SnapshotVersion);
    Equal(3, refreshed.RetryByRoute["route-a"].MaxRetries);
}

void TestServiceDiscoveryPluginId()
{
    Equal(ServiceDiscoveryRefreshService.PluginId, new HttpServiceDiscoveryPlugin().PluginId);
}

async Task TestServiceDiscoveryStaticRefreshAsync()
{
    var binding = new PluginBindingEntity
    {
        Id = "discovery-binding",
        PluginId = ServiceDiscoveryRefreshService.PluginId,
        PluginVersion = "1.0.0",
        Scope = PluginBindingScope.Cluster,
        ScopeId = "cluster-a",
        Enabled = true,
        ConfigJson = "{\"enabled\":true,\"mode\":\"Static\",\"staticEndpoints\":[\"http://127.0.0.1:5102\",\"http://127.0.0.1:5101\"]}"
    };
    var repository = new InMemoryPluginRepository([binding]);
    var compiler = new GatewaySnapshotCompiler(repository);
    var cluster = new ClusterConfig
    {
        ClusterId = "cluster-a",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["original"] = new() { Address = "http://127.0.0.1:5000" }
        }
    };
    var configProvider = new AneiangProxyConfigProvider([], [cluster]);
    var snapshot = await compiler.CompileAsync([], [cluster], 1);
    configProvider.Update(snapshot.Routes, snapshot.Clusters);
    var publisher = new GatewaySnapshotPublisher();
    publisher.Publish(snapshot);
    var plans = new GatewayPluginExecutionPlanProvider(publisher, NullLogger<GatewayPluginExecutionPlanProvider>.Instance);
    var service = new ServiceDiscoveryRefreshService(plans, configProvider, compiler, publisher,
        new ThrowingHttpClientFactory(), NullLogger<ServiceDiscoveryRefreshService>.Instance);

    await service.StartResourceAsync(CancellationToken.None);
    var deadline = DateTime.UtcNow.AddSeconds(3);
    while (publisher.Current.Version == 1 && DateTime.UtcNow < deadline) await Task.Delay(20);
    await service.StopResourceAsync(CancellationToken.None);

    Equal(2L, publisher.Current.Version);
    var addresses = publisher.Current.Clusters.Single().Destinations!.Values.Select(x => x.Address).Order().ToArray();
    True(addresses.SequenceEqual(new[] { "http://127.0.0.1:5101", "http://127.0.0.1:5102" }));
    Equal(0, ThrowingHttpClientFactory.CreateCalls);
}

async Task TestPluginHealthAndResourceContractsAsync()
{
    var healthProbe = new TestGatewayPlugin();
    var health = await healthProbe.CheckHealthAsync(CancellationToken.None);
    Equal(PluginHealthStatus.Healthy, health.Status);
    Equal("ready", health.Message);

    var resource = new TestRuntimeResource();
    var initial = await resource.CheckHealthAsync(CancellationToken.None);
    Equal(PluginResourceHealthStatus.Stopped, initial.Health);
    False(initial.Running);

    await resource.StartResourceAsync(CancellationToken.None);
    var running = await resource.CheckHealthAsync(CancellationToken.None);
    Equal(PluginResourceHealthStatus.Healthy, running.Health);
    True(running.Running);

    await resource.StopResourceAsync(CancellationToken.None);
    var stopped = await resource.CheckHealthAsync(CancellationToken.None);
    Equal(PluginResourceHealthStatus.Stopped, stopped.Health);
    False(stopped.Running);
}

void TestPluginSchemaValidation()
{
    var validator = new PluginConfigurationSchemaValidator();
    const string schema = "{\"type\":\"object\",\"required\":[\"settings\"],\"properties\":{\"settings\":{\"type\":\"object\",\"required\":[\"items\"],\"properties\":{\"items\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"required\":[\"name\"],\"properties\":{\"name\":{\"type\":\"string\"}}}}}},\"mode\":{\"oneOf\":[{\"const\":\"fast\"},{\"const\":\"safe\"}]},\"value\":{\"allOf\":[{\"type\":\"number\"},{\"minimum\":1}]},\"tag\":{\"anyOf\":[{\"type\":\"string\"},{\"type\":\"null\"}]}}}";
    True(validator.TryValidate("{\"settings\":{\"items\":[{\"name\":\"a\"}]},\"mode\":\"fast\",\"value\":2,\"tag\":null}", schema, out _, out _));
    False(validator.TryValidate("{\"settings\":{\"items\":[{}]},\"mode\":\"fast\",\"value\":2}", schema, out _, out var error));
    True(error.Contains("$.settings.items[0].name", StringComparison.Ordinal));
    False(validator.TryValidate("{\"settings\":{\"items\":[]},\"mode\":\"other\",\"value\":2}", schema, out _, out error));
    True(error.Contains("$.mode", StringComparison.Ordinal));
}

void TestNativeAdapters()
{
    var route = new RouteConfig { RouteId = "r", Match = new RouteMatch { Path = "/" } };
    var timeoutResult = NativePluginAdapters.ApplyRoute(route, [PluginSnapshot(NativePluginAdapters.RouteTimeout, PluginBindingScope.Route, "r", "{\"Timeout\":\"00:00:05\"}")]);
    Equal(TimeSpan.FromSeconds(5), timeoutResult.Timeout);
    True(route.Timeout is null);
    Throws<ArgumentException>(() => NativePluginAdapters.ApplyRoute(route, [PluginSnapshot(NativePluginAdapters.RouteTimeout, PluginBindingScope.Route, "r", "{\"Timeout\":\"00:00:00\"}")]));
    False(NativePluginAdapters.TryValidate(NativePluginAdapters.RouteTimeout, PluginBindingScope.Route, "{\"Timeout\":\"00:00:05\",\"Unknown\":true}", out _));
    var cluster = new ClusterConfig { ClusterId = "c", Destinations = new Dictionary<string, DestinationConfig>() };
    var lbResult = NativePluginAdapters.ApplyCluster(cluster, [PluginSnapshot(NativePluginAdapters.ClusterLoadBalancing, PluginBindingScope.Cluster, "c", "{\"LoadBalancingPolicy\":\"RoundRobin\"}")]);
    Equal("RoundRobin", lbResult.LoadBalancingPolicy);
    True(cluster.LoadBalancingPolicy is null);
}

void TestLegacyGlobalPolicyIsolation()
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddAneiangYarpDashboard();

    var registeredTypes = services.Select(descriptor => descriptor.ServiceType.FullName).ToHashSet(StringComparer.Ordinal);
    foreach (var legacyType in new[]
    {
        "Aneiang.Yarp.Dashboard.Modules.Policy.Services.IGatewayPolicyService",
        "Aneiang.Yarp.Dashboard.Modules.Waf.Services.IWafSettingsPersistenceService",
        "Aneiang.Yarp.Dashboard.Extensions.RateLimitConfigProvider",
        "Microsoft.AspNetCore.RateLimiting.RateLimiterOptions"
    })
    {
        False(registeredTypes.Contains(legacyType));
    }

    var pluginIds = services
        .Where(descriptor => descriptor.ServiceType == typeof(IGatewayPlugin))
        .Select(descriptor => descriptor.ImplementationType)
        .Where(type => type != null)
        .Select(type => type!.Name)
        .ToHashSet(StringComparer.Ordinal);
    foreach (var pluginType in new[] { "CircuitBreakerPlugin", "RequestRetryPlugin", "RateLimitPlugin", "WafPlugin" })
        True(pluginIds.Contains(pluginType));

    var publishedTools = new GatewayToolRegistry().GetToolDefinitions().Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
    foreach (var legacyTool in new[]
    {
        "get_waf_settings", "update_waf_settings", "get_policies", "create_cluster_policy",
        "apply_cluster_policy", "create_route_policy", "apply_route_policy", "delete_policy",
        "get_rate_limit_status", "get_retry_config"
    })
    {
        False(publishedTools.Contains(legacyTool));
    }
}

void Run(string name, Action action)
{
    try { action(); Console.WriteLine($"PASS: {name}"); }
    catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); }
}
async Task RunAsync(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine($"PASS: {name}"); }
    catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); }
}
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected '{expected}', got '{actual}'."); }
void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
void False(bool value) { if (value) throw new InvalidOperationException("Expected false."); }
static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Aneiang.Yarp.sln")))
            return directory.FullName;
    }

    throw new DirectoryNotFoundException("Could not locate Aneiang.Yarp.sln from regression test output directory.");
}
void Throws<TException>(Action action) where TException : Exception { try { action(); } catch (TException) { return; } throw new InvalidOperationException($"Expected {typeof(TException).Name}."); }
async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception { try { await action(); } catch (TException) { return; } throw new InvalidOperationException($"Expected {typeof(TException).Name}."); }
bool SatisfiesVersionConstraint(string actualText, string constraint) => TrySatisfyVersionConstraint(actualText, constraint, out _);
bool TrySatisfyVersionConstraint(string actualText, string constraint, out string? error)
{
    var dashboardAssembly = typeof(ExternalGatewayPluginHost).Assembly;
    var versionType = dashboardAssembly.GetType("Aneiang.Yarp.Dashboard.Infrastructure.Plugin.SemanticVersion", throwOnError: true)!;
    var constraintType = dashboardAssembly.GetType("Aneiang.Yarp.Dashboard.Infrastructure.Plugin.SemanticVersionConstraint", throwOnError: true)!;
    var tryParse = versionType.GetMethod("TryParse", BindingFlags.Public | BindingFlags.Static)!;
    var parseArguments = new object?[] { actualText, null };
    True((bool)tryParse.Invoke(null, parseArguments)!);
    var isSatisfied = constraintType.GetMethod("IsSatisfied", BindingFlags.Public | BindingFlags.Static)!;
    var constraintArguments = new[] { parseArguments[1], constraint, null };
    var result = (bool)isSatisfied.Invoke(null, constraintArguments)!;
    error = constraintArguments[2] as string;
    return result;
}

static PluginBindingEntity NewBinding(string id, PluginBindingScope scope, string scopeId, string pluginId, bool enabled, string json) => new()
{
    Id = id, Scope = scope, ScopeId = scopeId, PluginId = pluginId, Enabled = enabled, ConfigJson = json,
    SchemaVersion = 1, ConfigVersion = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
};
static PluginBindingSnapshot PluginSnapshot(string pluginId, PluginBindingScope scope, string scopeId, string json) => new(Guid.NewGuid().ToString("N"), pluginId, scope, scopeId, json, 1, 1, 0);
static GatewaySnapshot EmptySnapshot(long version) => new(version, DateTimeOffset.UtcNow, [], [], ImmutableDictionary<string, ImmutableArray<PluginBindingSnapshot>>.Empty, ImmutableDictionary<string, ImmutableArray<PluginBindingSnapshot>>.Empty, ImmutableDictionary<string, RouteExecutionPlan>.Empty, ImmutableDictionary<string, ClusterExecutionPlan>.Empty);
static GatewaySnapshot SnapshotWithRouteBinding(long version, string json) => new(
    version,
    DateTimeOffset.UtcNow,
    [new RouteConfig { RouteId = "route-a", Match = new RouteMatch { Path = "/" } }],
    [],
    ImmutableDictionary<string, ImmutableArray<PluginBindingSnapshot>>.Empty.Add(
        "route-a", [PluginSnapshot("request-retry", PluginBindingScope.Route, "route-a", json)]),
    ImmutableDictionary<string, ImmutableArray<PluginBindingSnapshot>>.Empty,
    ImmutableDictionary<string, RouteExecutionPlan>.Empty,
    ImmutableDictionary<string, ClusterExecutionPlan>.Empty);

sealed class TestGatewayPlugin : IGatewayPlugin, IPluginHealthProbe
{
    public string PluginId => "test-plugin";
    public string DisplayName => "Test Plugin";
    public string Version => "1.2.3";
    public PluginManifest Manifest { get; } = new("test-plugin", "Test Plugin", "1.2.3", [], [], 0, new(), [], "test");
    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null) { }
    public void ConfigureMiddleware(IApplicationBuilder app) { }
    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline) { }
    public ValueTask<PluginHealthProbeResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PluginHealthProbeResult(PluginHealthStatus.Healthy, "ready", DateTimeOffset.UtcNow));
}

sealed record RuntimeDomainMarker;

sealed class RuntimeDomainDrainMarker(TaskCompletionSource disposed) : IDisposable
{
    public void Dispose() => disposed.TrySetResult();
}

sealed class RuntimeDomainDrainPlugin(
    TaskCompletionSource entered,
    TaskCompletionSource release,
    TaskCompletionSource disposed) : IGatewayPlugin
{
    public string PluginId => "waf";
    public string DisplayName => PluginId;
    public string Version => "1.0.0";
    public PluginManifest Manifest { get; } = new("waf", "runtime-drain", "1.0.0", [], [], 0, new(), [], "test");
    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null) =>
        services.AddSingleton<RuntimeDomainDrainMarker>(_ => new RuntimeDomainDrainMarker(disposed));
    public void ConfigureMiddleware(IApplicationBuilder app)
    {
        var marker = app.ApplicationServices.GetRequiredService<RuntimeDomainDrainMarker>();
        app.Use(async (_, next) =>
        {
            GC.KeepAlive(marker);
            entered.TrySetResult();
            await release.Task;
            await next();
        });
    }
    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline) { }
}

sealed class RuntimeDomainTestPlugin(string pluginId, bool healthy) : IGatewayPlugin, IPluginHealthProbe
{
    public string PluginId => pluginId;
    public string DisplayName => pluginId;
    public string Version => "1.0.0";
    public PluginManifest Manifest { get; } = new(pluginId, pluginId, "1.0.0", [], [], 0, new(), [], "test");
    public void ConfigureServices(IServiceCollection services, object? pluginOptions = null) => services.AddSingleton<RuntimeDomainMarker>();
    public void ConfigureMiddleware(IApplicationBuilder app) { }
    public void ConfigureProxyPipeline(IReverseProxyApplicationBuilder proxyPipeline) { }
    public ValueTask<PluginHealthProbeResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PluginHealthProbeResult(
            healthy ? PluginHealthStatus.Healthy : PluginHealthStatus.Unhealthy,
            healthy ? "ready" : "failed",
            DateTimeOffset.UtcNow));
}

sealed class TestRuntimeResource : IPluginRuntimeResource
{
    private bool _running;
    public string PluginId => "test-plugin";
    public string ResourceId => "test-worker";
    public string ResourceType => "BackgroundWorker";

    public ValueTask StartResourceAsync(CancellationToken cancellationToken)
    {
        _running = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask StopResourceAsync(CancellationToken cancellationToken)
    {
        _running = false;
        return ValueTask.CompletedTask;
    }

    public ValueTask<PluginRuntimeResourceSnapshot> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var health = _running ? PluginResourceHealthStatus.Healthy : PluginResourceHealthStatus.Stopped;
        return ValueTask.FromResult(new PluginRuntimeResourceSnapshot(
            ResourceId,
            ResourceType,
            _running,
            health,
            _running ? DateTimeOffset.UtcNow : null,
            DateTimeOffset.UtcNow,
            null,
            new Dictionary<string, long>()));
    }
}

sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Aneiang.Yarp.Regression";
    public string ContentRootPath { get; set; } = contentRootPath;
    public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
}

sealed class ThrowingHttpClientFactory : IHttpClientFactory
{
    public static int CreateCalls { get; private set; }

    public HttpClient CreateClient(string name)
    {
        CreateCalls++;
        throw new InvalidOperationException("Static service discovery must not create an HTTP client.");
    }
}

sealed class ToggleActivationState : IPluginActivationState
{
    public bool Enabled { get; set; } = true;
    public bool IsPluginEnabled(string pluginId) => Enabled;
}

sealed record OrderedConfig(string Name, int Value);

sealed class OrderedRouteCompiler(int order, string name) : IRoutePluginCompiler
{
    public const string PluginId = "ordered-route";
    public int Order => order;
    public bool CanCompile(string pluginId) => string.Equals(pluginId, PluginId, StringComparison.OrdinalIgnoreCase);
    public CompiledRoutePlugin Compile(PluginBindingSnapshot binding, RouteConfig route)
    {
        var value = JsonDocument.Parse(binding.ConfigJson).RootElement.GetProperty("value").GetInt32();
        return new CompiledRoutePlugin(binding, PluginExecutionStage.PreProxy, new OrderedConfig(name, value), route);
    }
}

sealed class OrderedClusterCompiler(int order, string name) : IClusterPluginCompiler
{
    public const string PluginId = "ordered-cluster";
    public int Order => order;
    public bool CanCompile(string pluginId) => string.Equals(pluginId, PluginId, StringComparison.OrdinalIgnoreCase);
    public CompiledClusterPlugin Compile(PluginBindingSnapshot binding, ClusterConfig cluster)
    {
        var value = JsonDocument.Parse(binding.ConfigJson).RootElement.GetProperty("value").GetInt32();
        return new CompiledClusterPlugin(binding, PluginExecutionStage.PreProxy, new OrderedConfig(name, value), cluster);
    }
}

sealed class InMemoryRouteRepository(IReadOnlyList<RouteEntity> routes) : IRouteRepository
{
    public Task<RouteEntity?> GetRouteAsync(string routeId, CancellationToken ct = default) => Task.FromResult(routes.FirstOrDefault(x => x.RouteId == routeId));
    public Task<IReadOnlyList<RouteEntity>> GetAllRoutesAsync(CancellationToken ct = default) => Task.FromResult(routes);
    public Task<IReadOnlyList<RouteEntity>> GetRoutesByClusterAsync(string clusterId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RouteEntity>>(routes.Where(x => x.ClusterId == clusterId).ToArray());
    public Task SaveRouteAsync(RouteEntity route, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveRoutesAsync(IEnumerable<RouteEntity> values, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteRouteAsync(string routeId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteRoutesByClusterAsync(string clusterId, CancellationToken ct = default) => throw new NotSupportedException();
}

sealed class InMemoryClusterRepository(IReadOnlyList<ClusterEntity> clusters) : IClusterRepository
{
    public Task<ClusterEntity?> GetClusterAsync(string clusterId, CancellationToken ct = default) => Task.FromResult(clusters.FirstOrDefault(x => x.ClusterId == clusterId));
    public Task<IReadOnlyList<ClusterEntity>> GetAllClustersAsync(CancellationToken ct = default) => Task.FromResult(clusters);
    public Task SaveClusterAsync(ClusterEntity cluster, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveClustersAsync(IEnumerable<ClusterEntity> values, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteClusterAsync(string clusterId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<DestinationEntity>> GetDestinationsAsync(string clusterId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DestinationEntity>>([]);
    public Task SaveDestinationsAsync(string clusterId, IEnumerable<DestinationEntity> destinations, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteDestinationsAsync(string clusterId, CancellationToken ct = default) => throw new NotSupportedException();
}

sealed class InMemoryPluginRepository(IReadOnlyList<PluginBindingEntity> bindings) : IPluginConfigurationRepository
{
    public Task<IReadOnlyList<PluginBindingEntity>> GetBindingsAsync(CancellationToken ct = default) => Task.FromResult(bindings);
    public Task<IReadOnlyList<PluginBindingEntity>> GetBindingsAsync(PluginBindingScope scope, string scopeId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PluginBindingEntity>>(bindings.Where(x => x.Scope == scope && x.ScopeId == scopeId).ToArray());
    public Task<PluginBindingEntity?> GetBindingAsync(string id, CancellationToken ct = default) => Task.FromResult(bindings.FirstOrDefault(x => x.Id == id));
    public Task UpsertBindingAsync(PluginBindingEntity binding, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> DeleteBindingAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<PluginSchemaEntity>> GetSchemasAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PluginSchemaEntity>>([]);
    public Task<PluginSchemaEntity?> GetSchemaAsync(string pluginId, int schemaVersion, CancellationToken ct = default) => Task.FromResult<PluginSchemaEntity?>(null);
    public Task UpsertSchemaAsync(PluginSchemaEntity schema, CancellationToken ct = default) => throw new NotSupportedException();
}
