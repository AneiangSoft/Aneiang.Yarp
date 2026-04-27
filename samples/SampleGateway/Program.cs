using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Dashboard.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════
// 网关角色 — 一键注册全部所需服务
// ═══════════════════════════════════════════════════════════
builder.Services.AddAneiangYarpGateway();
builder.Services.AddAneiangYarpDashboard();  // 可选：运维仪表盘

var app = builder.Build();

// 网关中间件管道（进入 YARP 转发前的处理链）
// 在这里可以添加认证/授权/限流/CORS 等中间件
app.UseStaticFiles();
app.UseRouting();
app.MapControllers();
app.MapReverseProxy();

Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║  内网网关已启动                                  ║");
Console.WriteLine("║  仪表盘:  http://localhost:5000/apigateway        ║");
Console.WriteLine("║  注册API: POST /api/gateway/register-route        ║");
Console.WriteLine("║  流程:    client → 中间件 → YARP → 后端          ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");

app.Run();
