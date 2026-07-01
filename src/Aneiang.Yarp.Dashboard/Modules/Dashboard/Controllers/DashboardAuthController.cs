using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Infrastructure.I18n;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Authentication endpoints — login, logout, and two-factor authentication.
/// </summary>
public class DashboardAuthController : Controller
{
    private readonly IDashboardAuthorizationService _authService;

    private readonly string _defaultLocale;
    private readonly DashboardAuthMode _authMode;
    private readonly string? _jwtSecret;
    private readonly string? _jwtPassword;
    private readonly string? _jwtUsername;
    private readonly bool _enableTwoFactor;
    private readonly string? _twoFactorSecret;
    private readonly int _minPasswordLength;

    // Runtime 2FA state (persisted to file)
    private static readonly string _twoFactorStateFile = Path.Combine(AppContext.BaseDirectory, "twofactor-state.json");
    private static readonly object _twoFactorLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardAuthController"/> class.
    /// </summary>
    public DashboardAuthController(
        IDashboardAuthorizationService authService,
        IOptions<DashboardOptions> dashboardOptions)
    {
        _authService = authService;

        var opt = dashboardOptions.Value;
        _defaultLocale = opt.Locale;
        _authMode = opt.AuthMode;
        _jwtSecret = opt.JwtSecret;
        _jwtPassword = opt.JwtPassword;
        _jwtUsername = opt.JwtUsername;
        _enableTwoFactor = opt.EnableTwoFactor;
        _twoFactorSecret = opt.TwoFactorSecret;
        _minPasswordLength = opt.MinPasswordLength;
    }

    #region Login Page

    /// <summary>Dashboard login page.</summary>
    [HttpGet("login")]
    public IActionResult Login()
    {
        ViewBag.DashboardRoutePrefix = DashboardPagesController.RoutePrefix;
        ViewBag.AuthMode = _authMode;
        ViewBag.Locale = _defaultLocale == "en-US" ? "en-US" : "zh-CN";
        ViewBag.AllI18nJson = DashboardI18n.AllAsJson(ViewBag.Locale);
        return View();
    }

    // ── Login / Logout ──

    /// <summary>Login POST — validate credentials and return JWT.</summary>
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Json(new { code = 400, message = "Username and password are required" });

        if (request.Password.Length < _minPasswordLength)
            return Json(new { code = 400, message = $"Password must be at least {_minPasswordLength} characters" });

        bool valid = _authMode switch
        {
            DashboardAuthMode.CustomJwt =>
                request.Username == _jwtUsername && request.Password == _jwtPassword,
            DashboardAuthMode.DefaultJwt =>
                request.Username == "admin" && request.Password == _jwtPassword,
            _ => false
        };

        if (!valid)
            return Json(new { code = 401, message = "Invalid credentials" });

        // 2FA verification
        var (twoFactorEnabled, twoFactorSecret) = GetTwoFactorState();
        if (twoFactorEnabled && !string.IsNullOrWhiteSpace(twoFactorSecret))
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
                return Json(new { code = 202, message = "Two-factor authentication required", requiresTwoFactor = true });

            if (!TotpHelper.ValidateCode(twoFactorSecret, request.TwoFactorCode))
                return Json(new { code = 401, message = "Invalid two-factor code" });
        }

        var token = DashboardJwtHelper.GenerateToken(request.Username, _jwtSecret!);

        Response.Cookies.Append("dashboard_token", token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTime.Now.AddHours(8)
        });

        return Json(new { code = 200, token });
    }

    /// <summary>Logout — clear the auth token cookie.</summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("dashboard_token");
        return Json(new { code = 200, message = "Logged out successfully" });
    }

    #endregion

    #region Two-Factor Authentication

    /// <summary>Get 2FA status.</summary>
    [HttpGet("api/2fa/status")]
    public IActionResult GetTwoFactorStatus()
    {
        var (enabled, _) = GetTwoFactorState();
        return Json(new { code = 200, data = new { enabled, minPasswordLength = _minPasswordLength } });
    }

    /// <summary>Generate a new 2FA secret and QR URL.</summary>
    [HttpGet("api/2fa/setup")]
    public IActionResult SetupTwoFactor()
    {
        var secret = TotpHelper.GenerateSecret();
        var issuer = "Gateway Dashboard";
        var account = _jwtUsername ?? "admin";
        var qrUrl = TotpHelper.BuildOtpAuthUri(issuer, account, secret);
        return Json(new { code = 200, data = new { secret, qrUrl } });
    }

    /// <summary>Verify 2FA code and enable 2FA.</summary>
    [HttpPost("api/2fa/verify")]
    public IActionResult VerifyTwoFactor([FromBody] JsonElement body)
    {
        var code = body.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
        var secret = body.TryGetProperty("secret", out var secretEl) ? secretEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(secret))
            return Json(new { code = 400, message = "Code and secret are required" });

        if (!TotpHelper.ValidateCode(secret, code))
            return Json(new { code = 400, message = "Invalid two-factor code" });

        SaveTwoFactorState(true, secret);
        return Json(new { code = 200, message = "Two-factor authentication enabled" });
    }

    /// <summary>Disable 2FA.</summary>
    [HttpPost("api/2fa/disable")]
    public IActionResult DisableTwoFactor()
    {
        SaveTwoFactorState(false, null);
        return Json(new { code = 200, message = "Two-factor authentication disabled" });
    }

    // ── 2FA State Persistence ──

    private (bool enabled, string? secret) GetTwoFactorState()
    {
        try
        {
            if (System.IO.File.Exists(_twoFactorStateFile))
            {
                var json = System.IO.File.ReadAllText(_twoFactorStateFile);
                var state = System.Text.Json.JsonSerializer.Deserialize<TwoFactorState>(json);
                if (state != null)
                    return (state.Enabled, state.Secret);
            }
        }
        catch { }

        return (_enableTwoFactor, _twoFactorSecret);
    }

    private void SaveTwoFactorState(bool enabled, string? secret)
    {
        lock (_twoFactorLock)
        {
            try
            {
                var state = new TwoFactorState { Enabled = enabled, Secret = secret };
                var json = System.Text.Json.JsonSerializer.Serialize(state);
                System.IO.File.WriteAllText(_twoFactorStateFile, json);
            }
            catch { }
        }
    }

    private class TwoFactorState
    {
        public bool Enabled { get; set; }
        public string? Secret { get; set; }
    }

    #endregion

    #region Auth Status

    /// <summary>Get current authorization status and mode.</summary>
    [HttpGet("api/auth/status")]
    public IActionResult GetAuthStatus()
    {
        var authModeDescription = _authService.GetAuthModeDescription();
        var isAuthEnabled = _authMode != DashboardAuthMode.None;

        return Json(new
        {
            code = 200,
            data = new
            {
                IsAuthEnabled = isAuthEnabled,
                AuthMode = _authMode.ToString(),
                AuthModeDescription = authModeDescription,
                Locale = _defaultLocale
            }
        });
    }

    #endregion
}
