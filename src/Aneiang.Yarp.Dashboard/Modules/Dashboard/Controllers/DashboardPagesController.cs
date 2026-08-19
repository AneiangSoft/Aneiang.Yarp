using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Aneiang.Yarp.Dashboard.Infrastructure.I18n;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Plugin.ProxyLog.Services;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// MVC view pages for the dashboard UI (10 pages + DB download).
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
    private readonly DeploymentOptions _deploymentOptions;

    // Cached option values
    private readonly string _defaultLocale;
    private readonly string _authMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardPagesController"/> class.
    /// </summary>
    public DashboardPagesController(
        IDashboardInfoQueryService infoQuery,
        IDashboardClusterQueryService clusterQuery,
        IDashboardRouteQueryService routeQuery,
        IDashboardLogQueryService logQuery,
        IOptions<DashboardOptions> dashboardOptions,
        IOptions<StorageOptions> storageOptions,
        IOptions<DeploymentOptions> deploymentOptions)
    {
        _infoQuery = infoQuery;
        _clusterQuery = clusterQuery;
        _routeQuery = routeQuery;
        _logQuery = logQuery;
        _storageOptions = storageOptions.Value;
        _deploymentOptions = deploymentOptions.Value;

        _defaultLocale = dashboardOptions.Value.Locale;
        _authMode = dashboardOptions.Value.Auth.AuthMode.ToString();
    }

    /// <summary>
    /// Sets common ViewBag properties.
    /// </summary>
    private void SetCommonViewBag(string? currentPage = null)
    {
        ViewBag.DashboardRoutePrefix = RoutePrefix;
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

    #region View pages

    /// <summary>Overview page.</summary>
    [HttpGet("")]
    public IActionResult Overview() { SetCommonViewBag("overview"); return View(); }

    [HttpGet("clusters")]
    public IActionResult Clusters() { SetCommonViewBag("clusters"); return View(); }

    [HttpGet("routes")]
    public IActionResult Routes() { SetCommonViewBag("routes"); return View(); }

    [HttpGet("history")]
    public IActionResult History() { SetCommonViewBag("history"); return View(); }

    [HttpGet("plugins")]
    public IActionResult Plugins() { SetCommonViewBag("plugins"); return View(); }

    [HttpGet("audit")]
    public IActionResult Audit() { SetCommonViewBag("audit"); return View(); }

    [HttpGet("settings")]
    public IActionResult Settings() { SetCommonViewBag("settings"); return View(); }

    [HttpGet("plugin-resources")]
    public IActionResult PluginResources() { SetCommonViewBag("plugin-resources"); return View(); }

    [HttpGet("plugin-center")]
    public IActionResult PluginCenter() { SetCommonViewBag("plugin-center"); return View(); }

    [HttpGet("logs")]
    public IActionResult Logs() { SetCommonViewBag("logs"); return View(); }

    [HttpGet("traffic")]
    public IActionResult Traffic() { SetCommonViewBag("traffic"); return View(); }

    [HttpGet("cluster-metrics")]
    public IActionResult ClusterMetrics() { SetCommonViewBag("cluster-metrics"); return View(); }

    [HttpGet("circuits")]
    public IActionResult Circuits() { SetCommonViewBag("circuits"); return View(); }

    #endregion

    #region DB Download

    /// <summary>System information shown on the settings page.</summary>
    [HttpGet("api/settings/system")]
    public IActionResult GetSystemInfo()
    {
        var dbPath = ResolveDatabasePath(_storageOptions);
        long dbSize = 0;
        if (!string.IsNullOrEmpty(dbPath) && System.IO.File.Exists(dbPath))
        {
            try { dbSize = new FileInfo(dbPath).Length; }
            catch { /* ignore */ }
        }

        return Json(new
        {
            code = 200,
            data = new
            {
                version = _infoQuery.GetInfo().Version,
                routePrefix = RoutePrefix,
                authMode = _authMode,
                locale = _defaultLocale,
                databaseFile = dbPath,
                databaseSizeBytes = dbSize,
                deployment = new
                {
                    mode = _deploymentOptions.Mode.ToString(),
                    autoMiddleware = _deploymentOptions.AutoUseMiddleware,
                    requireLoopbackAdmin = _deploymentOptions.RequireLoopbackForAdmin,
                    requireLoopbackDashboard = _deploymentOptions.RequireLoopbackForDashboard,
                    endpoints = _deploymentOptions.ResolvedEndpoints.Select(e => new
                    {
                        name = e.EndpointName,
                        port = e.Port,
                        address = e.IpAddress,
                        role = e.Role,
                        isPublic = e.IsPubliclyBound
                    }).ToList(),
                    healthCheck = new
                    {
                        enabled = _deploymentOptions.HealthCheck.Enabled,
                        path = _deploymentOptions.HealthCheck.Path,
                        readyPath = _deploymentOptions.HealthCheck.ReadyPath,
                        livePath = _deploymentOptions.HealthCheck.LivePath,
                        checkDatabase = _deploymentOptions.HealthCheck.CheckDatabase,
                        checkConfigLoaded = _deploymentOptions.HealthCheck.CheckConfigLoaded
                    }
                }
            }
        });
    }

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

    /// <summary>Create a consistent snapshot backup via VACUUM INTO and stream for download.</summary>
    [HttpGet("api/settings/database/backup")]
    public async Task<IActionResult> BackupDatabase()
    {
        var dbPath = ResolveDatabasePath(_storageOptions);
        if (!System.IO.File.Exists(dbPath))
            return Json(new { code = 404, message = "Database file not found" });

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var tempPath = Path.Combine(Path.GetTempPath(), $"gw-backup-{timestamp}-{Guid.NewGuid():N}.db");

        try
        {
            var connStr = _storageOptions.Sqlite.ConnectionString;
            using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{tempPath.Replace("'", "''")}'";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Fallback: copy the file if VACUUM INTO is unavailable
            System.IO.File.Copy(dbPath, tempPath, true);
        }

        var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
        var fileName = $"gateway-store-backup-{timestamp}.db";
        return File(stream, "application/octet-stream", fileName);
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
