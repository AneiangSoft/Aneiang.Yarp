using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.I18n;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// MVC view pages for the dashboard UI (17 pages + DB download).
/// </summary>
public class DashboardPagesController : Controller
{
    /// <summary>Route prefix, set by convention at startup.</summary>
    internal static string RoutePrefix { get; set; } = "apigateway";

    private readonly IDashboardInfoQueryService _infoQuery;
    private readonly IDashboardClusterQueryService _clusterQuery;
    private readonly IDashboardRouteQueryService _routeQuery;
    private readonly IDashboardLogQueryService _logQuery;
    private readonly StorageOptions _storageOptions;

    // Cached option values
    private readonly bool _enableProxyLogging;
    private readonly string _defaultLocale;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardPagesController"/> class.
    /// </summary>
    public DashboardPagesController(
        IDashboardInfoQueryService infoQuery,
        IDashboardClusterQueryService clusterQuery,
        IDashboardRouteQueryService routeQuery,
        IDashboardLogQueryService logQuery,
        IOptions<DashboardOptions> dashboardOptions,
        IOptions<StorageOptions> storageOptions)
    {
        _infoQuery = infoQuery;
        _clusterQuery = clusterQuery;
        _routeQuery = routeQuery;
        _logQuery = logQuery;
        _storageOptions = storageOptions.Value;

        var opt = dashboardOptions.Value;
        _enableProxyLogging = opt.EnableProxyLogging;
        _defaultLocale = opt.Locale;
    }

    /// <summary>
    /// Sets common ViewBag properties.
    /// </summary>
    private void SetCommonViewBag(string? currentPage = null)
    {
        ViewBag.DashboardRoutePrefix = RoutePrefix;
        ViewBag.EnableProxyLogging = _enableProxyLogging;
        ViewBag.Locale = ResolveLocale();
        ViewBag.AllI18nJson = DashboardI18n.AllAsJson(ViewBag.Locale);
        ViewBag.CurrentPage = currentPage ?? "overview";
    }

    /// <summary>
    /// Resolves the locale from cookie or default.
    /// </summary>
    private string ResolveLocale()
    {
        var cookieLocale = Request.Cookies["dashboard_locale"];
        if (!string.IsNullOrEmpty(cookieLocale))
            return cookieLocale == "en-US" ? "en-US" : "zh-CN";
        return _defaultLocale == "en-US" ? "en-US" : "zh-CN";
    }

    #region 17 View pages

    /// <summary>Overview page.</summary>
    [HttpGet("")]
    public IActionResult Overview() { SetCommonViewBag("overview"); return View(); }

    [HttpGet("clusters")]
    public IActionResult Clusters() { SetCommonViewBag("clusters"); return View(); }

    [HttpGet("routes")]
    public IActionResult Routes() { SetCommonViewBag("routes"); return View(); }

    [HttpGet("stats")]
    public IActionResult Stats() { SetCommonViewBag("stats"); return View(); }

    [HttpGet("logs")]
    public IActionResult Logs() { SetCommonViewBag("logs"); return View(); }

    [HttpGet("circuits")]
    public IActionResult Circuits() { SetCommonViewBag("circuits"); return View(); }

    [HttpGet("notifications")]
    public IActionResult Notifications() { SetCommonViewBag("notifications"); return View(); }

    [HttpGet("waf")]
    public IActionResult Waf() { SetCommonViewBag("waf"); return View(); }

    [HttpGet("healthcheck")]
    public IActionResult HealthCheck() { SetCommonViewBag("healthcheck"); return View(); }

    [HttpGet("history")]
    public IActionResult History() { SetCommonViewBag("history"); return View(); }

    [HttpGet("policies")]
    public IActionResult Policies() { SetCommonViewBag("policies"); return View(); }

    [HttpGet("plugins")]
    public IActionResult Plugins() { SetCommonViewBag("plugins"); return View(); }

    [HttpGet("audit")]
    public IActionResult Audit() { SetCommonViewBag("audit"); return View(); }

    [HttpGet("settings")]
    public IActionResult Settings() { SetCommonViewBag("settings"); return View(); }

    [HttpGet("deployment")]
    public IActionResult Deployment() { SetCommonViewBag("deployment"); return View(); }

    [HttpGet("ai-settings")]
    public IActionResult AISettings() { SetCommonViewBag("ai"); return View("AISettings"); }

    #endregion

    #region DB Download

    /// <summary>Download the SQLite database file for local inspection.</summary>
    [HttpGet("api/settings/database")]
    public IActionResult DownloadDatabase()
    {
        var dbPath = ResolveDatabasePath(_storageOptions);
        if (!System.IO.File.Exists(dbPath))
            return Json(new { code = 404, message = "Database file not found" });

        var fileName = Path.GetFileName(dbPath);
        return PhysicalFile(dbPath, "application/octet-stream", fileName);
    }

    /// <summary>Resolves the SQLite database file path from the connection string.</summary>
    private static string ResolveDatabasePath(StorageOptions storageOptions)
    {
        var cs = storageOptions.Sqlite.ConnectionString;
        const string prefix = "Data Source=";

        int idx = cs.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;

        var value = cs[(idx + prefix.Length)..].Trim();

        int semi = value.IndexOf(';');
        if (semi >= 0)
            value = value[..semi].Trim();

        if (!Path.IsPathRooted(value))
        {
            var rooted = Path.GetFullPath(value);
            if (System.IO.File.Exists(rooted))
                return rooted;
        }

        return value;
    }

    #endregion
}
