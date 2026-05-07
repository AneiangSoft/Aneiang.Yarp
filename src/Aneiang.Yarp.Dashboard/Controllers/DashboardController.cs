using System.Reflection;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Models.Dtos;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>Gateway maintenance dashboard.</summary>
public class DashboardController : Controller
{
    /// <summary>Route prefix, set by convention at startup.</summary>
    internal static string RoutePrefix { get; set; } = "apigateway";

    private readonly IDashboardInfoQueryService _infoQuery;
    private readonly IDashboardClusterQueryService _clusterQuery;
    private readonly IDashboardRouteQueryService _routeQuery;
    private readonly IDashboardLogQueryService _logQuery;
    private readonly IDashboardAuthorizationService _authService;
    private readonly DashboardOptions _options;

    /// <summary>Initializes a new instance of DashboardController.</summary>
    /// <param name="infoQuery">Dashboard info query service.</param>
    /// <param name="clusterQuery">Cluster query service.</param>
    /// <param name="routeQuery">Route query service.</param>
    /// <param name="logQuery">Log query service.</param>
    /// <param name="authService">Authorization service.</param>
    /// <param name="options">Dashboard options.</param>
    public DashboardController(
        IDashboardInfoQueryService infoQuery,
        IDashboardClusterQueryService clusterQuery,
        IDashboardRouteQueryService routeQuery,
        IDashboardLogQueryService logQuery,
        IDashboardAuthorizationService authService,
        IOptions<DashboardOptions> options)
    {
        _infoQuery = infoQuery;
        _clusterQuery = clusterQuery;
        _routeQuery = routeQuery;
        _logQuery = logQuery;
        _authService = authService;
        _options = options.Value;
    }

    /// <summary>Dashboard home page.</summary>
    [HttpGet("")]
    public IActionResult Index()
    {
        ViewBag.DashboardRoutePrefix = RoutePrefix;
        ViewBag.EnableProxyLogging = _options.EnableProxyLogging;
        ViewBag.Locale = ResolveLocale();
        ViewBag.AllI18nJson = DashboardI18n.AllAsJson(ViewBag.Locale);
        return View();
    }

    /// <summary>Dashboard login page.</summary>
    [HttpGet("login")]
    public IActionResult Login()
    {
        ViewBag.DashboardRoutePrefix = RoutePrefix;
        ViewBag.AuthMode = _options.AuthMode;
        ViewBag.Locale = ResolveLocale();
        ViewBag.AllI18nJson = DashboardI18n.AllAsJson(ViewBag.Locale);
        return View();
    }

    private string ResolveLocale()
    {
        // Check cookie first (set by client-side switch), then config default
        var cookieLocale = Request.Cookies["dashboard_locale"];
        if (!string.IsNullOrEmpty(cookieLocale))
            return cookieLocale == "en-US" ? "en-US" : "zh-CN";
        return _options.Locale == "en-US" ? "en-US" : "zh-CN";
    }

    /// <summary>Login POST - validate credentials and return JWT.</summary>
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Json(new { code = 400, message = "Username and password are required" });

        bool valid = _options.AuthMode switch
        {
            DashboardAuthMode.CustomJwt =>
                request.Username == _options.JwtUsername && request.Password == _options.JwtPassword,
            DashboardAuthMode.DefaultJwt =>
                request.Username == "admin" && request.Password == _options.JwtPassword,
            _ => false
        };

        if (!valid)
            return Json(new { code = 401, message = "Invalid credentials" });

        var token = DashboardJwtHelper.GenerateToken(request.Username, _options.JwtSecret!);

        Response.Cookies.Append("dashboard_token", token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(8)
        });

        return Json(new { code = 200, token });
    }

    /// <summary>Gateway basic info.</summary>
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        var info = _infoQuery.GetInfo();
        return Json(new { code = 200, data = info });
    }

    /// <summary>Cluster status and config.</summary>
    [HttpGet("clusters")]
    public IActionResult GetClusters()
    {
        var clusters = _clusterQuery.GetClusters();
        return Json(new { code = 200, data = clusters });
    }

    /// <summary>Route configuration.</summary>
    [HttpGet("routes")]
    public IActionResult GetRoutes()
    {
        var routes = _routeQuery.GetRoutes();
        return Json(new { code = 200, data = routes });
    }

    /// <summary>Recent YARP proxy logs.</summary>
    [HttpGet("logs")]
    public IActionResult GetLogs([FromQuery] int count = 100)
    {
        var snapshot = _logQuery.GetLogs(count);
        return Json(new { code = 200, data = snapshot });
    }

    /// <summary>Clear all logs.</summary>
    [HttpDelete("logs")]
    public IActionResult ClearLogs()
    {
        _logQuery.ClearLogs();
        return Json(new { code = 200, message = "Logs cleared" });
    }

    /// <summary>Get current authorization status and mode.</summary>
    [HttpGet("auth/status")]
    public IActionResult GetAuthStatus()
    {
        var authModeDescription = _authService.GetAuthModeDescription();
        var isAuthEnabled = _options.AuthMode != DashboardAuthMode.None || _options.AuthorizeRequest != null;

        return Json(new
        {
            code = 200,
            data = new
            {
                isAuthEnabled,
                authMode = _options.AuthMode.ToString(),
                authModeDescription,
                locale = _options.Locale
            }
        });
    }

    /// <summary>Logout - clear the auth token cookie.</summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("dashboard_token");
        return Json(new { code = 200, message = "Logged out successfully" });
    }
}
