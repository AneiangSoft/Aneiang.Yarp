using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Microsoft.Extensions.Options;
using Serilog;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Yarp.ReverseProxy", Serilog.Events.LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[Serilog] {Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting SampleGateway with Serilog...");
    
    var builder = WebApplication.CreateBuilder(args);

    // Replace default logger with Serilog
    builder.Host.UseSerilog();

    // Enable HTTP/2 on all endpoints (required for gRPC)
    // When Kestrel:Endpoints has 2+ entries, auto-injection of the +1 gRPC port is
    // skipped automatically, so role-based routing keeps working.
    builder.UseYarpKestrelAutoConfig();

    // Gateway: one-liner — auto-loads ReverseProxy routes/clusters + dynamic config
    builder.Services.AddAneiangYarp();

    // Dashboard with JWT auth (DefaultJwt: username=admin, password from config)
    builder.Services.AddAneiangYarpDashboard();

    // Multi-port / split / proxy-only deployment support (optional, backward compatible)
    // Wires up deployment options, endpoint role resolver, validators, hot-reload, etc.
    builder.Services.AddAneiangYarpDeployment();

    // Optional: enable gRPC endpoint auth (shares Dashboard credentials by default)
    // builder.Services.AddGatewayApiAuth();

    var app = builder.Build();

    app.UseRouting();

    // Register Dashboard middleware + MapReverseProxy (mode-aware).
    // Deployment-aware routing and health-check middleware are mounted automatically when deployment services are registered.
    app.UseAneiangYarpDashboard();

    app.MapControllers();
    app.MapAneiangYarpGrpc(); // gRPC GatewayRegistry service (HTTP/2)

    // Log startup summary
    var depOptions = app.Services.GetRequiredService<IOptions<DeploymentOptions>>().Value;
    Console.WriteLine("======================================================");
    Console.WriteLine("  Internal gateway is running");
    Console.WriteLine($"  Mode:               {depOptions.Mode}");
    foreach (var ep in depOptions.ResolvedEndpoints)
        Console.WriteLine($"  Endpoint:           {ep.EndpointName} → {ep.IpAddress}:{ep.Port} ({ep.Role})");
    Console.WriteLine("  Dashboard:          /apigateway");
    Console.WriteLine("  Login:              /apigateway/login");
    Console.WriteLine("  Credentials:        admin / demo123");
    Console.WriteLine("  Health:             /health, /ready, /live");
    Console.WriteLine("  Logger:             Serilog");
    Console.WriteLine("  gRPC:               HTTP/2 enabled");
    Console.WriteLine("======================================================");

    Log.Information("Gateway started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Gateway terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
