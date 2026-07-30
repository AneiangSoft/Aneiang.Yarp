using System.Collections.Concurrent;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:5300");
builder.Services.AddSingleton<BackendState>();
var app = builder.Build();

var smallJson = JsonSerializer.Serialize(new
{
    id = 1,
    name = "Aneiang.Yarp performance payload",
    timestamp = "2026-01-01T00:00:00Z",
    values = Enumerable.Range(1, 32).ToArray()
});

app.Use(async (context, next) =>
{
    await next();
    if (context.Request.Path.StartsWithSegments("/api/perf") &&
        !context.Request.Path.StartsWithSegments("/api/perf/control"))
    {
        context.RequestServices.GetRequiredService<BackendState>()
            .Record(context.Request.Path, context.Response.StatusCode);
    }
});

app.MapGet("/health", () => Results.Ok());
app.MapGet("/api/perf/plain", () => Results.Text("OK", "text/plain"));
app.MapGet("/api/perf/json-small", () => Results.Text(smallJson, "application/json"));
app.MapGet("/api/perf/payload/{size:int}", (int size) =>
{
    var length = Math.Clamp(size, 0, 4 * 1024 * 1024);
    return Results.Text(new string('x', length), "application/octet-stream");
});
app.MapPost("/api/perf/echo", async (HttpRequest request) =>
{
    using var memory = new MemoryStream();
    await request.Body.CopyToAsync(memory);
    return Results.Bytes(memory.ToArray(), request.ContentType ?? "application/octet-stream");
});
app.MapGet("/api/perf/delay/{milliseconds:int}", async (int milliseconds) =>
{
    await Task.Delay(Math.Clamp(milliseconds, 0, 30_000));
    return Results.Text("OK");
});
app.MapGet("/api/perf/status/{statusCode:int}", (int statusCode) => Results.StatusCode(Math.Clamp(statusCode, 100, 599)));
app.MapGet("/api/perf/flaky/{failCount:int}", (int failCount, BackendState state) =>
    state.NextFlakyAttempt() <= Math.Max(0, failCount)
        ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
        : Results.Text("OK", "text/plain"));
app.MapGet("/api/perf/control/counters", (BackendState state) => Results.Json(state.Snapshot()));
app.MapPost("/api/perf/control/reset", (BackendState state) =>
{
    state.Reset();
    return Results.NoContent();
});

app.Run();

sealed class BackendState
{
    private readonly ConcurrentDictionary<string, long> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, long> _statuses = new();
    private long _total;
    private long _flakyAttempts;

    public long NextFlakyAttempt() => Interlocked.Increment(ref _flakyAttempts);

    public void Record(string path, int statusCode)
    {
        Interlocked.Increment(ref _total);
        _paths.AddOrUpdate(path, 1, static (_, count) => count + 1);
        _statuses.AddOrUpdate(statusCode, 1, static (_, count) => count + 1);
    }

    public object Snapshot() => new
    {
        total = Interlocked.Read(ref _total),
        flakyAttempts = Interlocked.Read(ref _flakyAttempts),
        paths = _paths.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value),
        statuses = _statuses.OrderBy(x => x.Key).ToDictionary(x => x.Key.ToString(), x => x.Value)
    };

    public void Reset()
    {
        Interlocked.Exchange(ref _total, 0);
        Interlocked.Exchange(ref _flakyAttempts, 0);
        _paths.Clear();
        _statuses.Clear();
    }
}
