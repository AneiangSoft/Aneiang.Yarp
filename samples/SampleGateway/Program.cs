using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Gateway: one-liner — auto-loads ReverseProxy routes/clusters + dynamic config
builder.Services.AddAneiangYarp();

// Dashboard with JWT auth (DefaultJwt: username=admin, password from config)
builder.Services.AddAneiangYarpDashboard();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapControllers();
app.MapReverseProxy();

Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║  Internal gateway is running                     ║");
Console.WriteLine("║  Dashboard:           /apigateway                ║");
Console.WriteLine("║  Login:               /apigateway/login          ║");
Console.WriteLine("║  Credentials:         admin / demo123            ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");

app.Run();
