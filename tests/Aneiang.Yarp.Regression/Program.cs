using System.Net;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var failures = new List<string>();

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
    var options = Options.Create(new DashboardOptions
    {
        AuthMode = DashboardAuthMode.ApiKey,
        ApiKey = "secret",
        ApiKeyHeaderName = "X-Api-Key"
    });
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
    var service = new DashboardAuthorizationService(
        Options.Create(new DashboardOptions { AuthMode = DashboardAuthMode.CustomJwt, JwtSecret = secret }),
        NullLogger<DashboardAuthorizationService>.Instance);

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

Run("Proxy log runtime settings publish atomically", () =>
{
    var runtime = new ProxyLogRuntimeSettings(Options.Create(new DashboardOptions()));
    runtime.Update(new LogSettingsData
    {
        LogPersistenceEnabled = true,
        LogMetaRetentionDays = 7,
        LogBodyRetentionDays = 2,
        EnableProxyRequestBodyCapture = true,
        EnableProxyResponseBodyCapture = false,
        LogMaxBodyLength = 8192,
        LogMaxBodyBufferBytes = 16384,
        EnableLogSampling = true,
        LogSamplingRate = 0.25,
        LogErrorsOnly = true,
        MinLogLevel = "Warning"
    });

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

if (failures.Count > 0)
{
    Console.Error.WriteLine($"Regression checks failed: {failures.Count}");
    foreach (var failure in failures) Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine("All regression checks passed.");
return 0;

void Run(string name, Action action)
{
    try
    {
        action();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
    }
}

async Task RunAsync(string name, Func<Task> action)
{
    try
    {
        await action();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
    }
}

void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

void True(bool value)
{
    if (!value) throw new InvalidOperationException("Expected true.");
}

void False(bool value)
{
    if (value) throw new InvalidOperationException("Expected false.");
}
