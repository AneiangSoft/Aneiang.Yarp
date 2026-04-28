using Aneiang.Yarp.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Client: one-liner — auto-register on startup, auto-unregister on shutdown
// Minimal config: just GatewayUrl in code or appsettings.json
builder.Services.AddAneiangYarpClient();

// Or with code override (higher priority than config file):
// builder.Services.AddAneiangYarpClient(options =>
// {
//     options.GatewayUrl = "http://192.168.1.100:5000";
//     options.MatchPath = "/api/my-service/{**catch-all}";
//     options.Order = 50;
// });

// Your business services
builder.Services.AddControllers();

var app = builder.Build();
app.UseRouting();
app.MapControllers();

Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║  Local service is running                        ║");
Console.WriteLine("║  Flow: client → gateway middleware → YARP → svc  ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");

app.Run();
