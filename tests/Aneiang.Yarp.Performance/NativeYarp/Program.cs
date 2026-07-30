var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:5301");

builder.Services.AddReverseProxy().LoadFromMemory(
    [new Yarp.ReverseProxy.Configuration.RouteConfig
    {
        RouteId = "perf",
        ClusterId = "backend",
        Match = new Yarp.ReverseProxy.Configuration.RouteMatch { Path = "/api/perf/{**catch-all}" }
    }],
    [new Yarp.ReverseProxy.Configuration.ClusterConfig
    {
        ClusterId = "backend",
        Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
        {
            ["backend"] = new() { Address = "http://127.0.0.1:5300" }
        }
    }]);

var app = builder.Build();
app.MapGet("/health", () => Results.Ok());
app.MapReverseProxy();
app.Run();
