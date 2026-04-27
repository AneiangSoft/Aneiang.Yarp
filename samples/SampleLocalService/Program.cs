using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Models;

var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════
// 客户端角色 — 一键注册 + 自动启动注册 / 关闭注销
// ═══════════════════════════════════════════════════════════
// 最少只需配置一个 GatewayUrl（代码或配置文件均可）：
builder.Services.AddAneiangYarpClient();

// 使用方式 2 — 代码内自定义（优先级高于配置文件）：
// builder.Services.AddAneiangYarpClient(options =>
// {
//     options.GatewayUrl = "http://192.168.1.100:5000";
//     options.MatchPath = "/api/my-service/{**catch-all}";
//     options.Order = 50;
// });

// ── 您的单体服务 ──
builder.Services.AddControllers();

var app = builder.Build();
app.UseRouting();
app.MapControllers();

Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║  本地服务已启动                                  ║");
Console.WriteLine("║  流程: client → 网关中间件 → YARP → 本服务     ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");

app.Run();
