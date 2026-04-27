using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════
// Gateway role - one-liner registration of all required services
// ═══════════════════════════════════════════════════════════
builder.Services.AddAneiangYarpGateway();

// Dashboard with JWT authorization (DefaultJwt: username=admin, password from config)
builder.Services.AddAneiangYarpDashboard();   // Config in appsettings.json

var app = builder.Build();

// Gateway middleware pipeline
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
