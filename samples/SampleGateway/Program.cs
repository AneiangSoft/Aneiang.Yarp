using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;
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

    // Gateway: one-liner — auto-loads ReverseProxy routes/clusters + dynamic config
    builder.Services.AddAneiangYarp();

    // Dashboard with JWT auth (DefaultJwt: username=admin, password from config)
    builder.Services.AddAneiangYarpDashboard();

    var app = builder.Build();

    app.UseStaticFiles();
    app.UseRouting();

    // Register Dashboard middleware (request capture, auth, etc.)
    app.UseAneiangYarpDashboard();

    app.MapControllers();
    app.MapReverseProxy();

    Console.WriteLine("╔══════════════════════════════════════════════════╗");
    Console.WriteLine("║  Internal gateway is running                     ║");
    Console.WriteLine("║  Dashboard:           /apigateway                ║");
    Console.WriteLine("║  Login:               /apigateway/login          ║");
    Console.WriteLine("║  Credentials:         admin / demo123            ║");
    Console.WriteLine("║  Logger:              Serilog                    ║");
    Console.WriteLine("╚══════════════════════════════════════════════════╝");

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
