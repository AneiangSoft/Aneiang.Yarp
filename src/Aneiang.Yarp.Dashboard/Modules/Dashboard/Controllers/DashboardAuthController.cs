using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Infrastructure.Exceptions;
using Aneiang.Yarp.Dashboard.Infrastructure.I18n;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Authentication endpoints - login, logout, and two-factor authentication.
/// Uses <see cref="IStateStore"/> for 2FA state persistence.
/// </summary>
public class DashboardAuthController : Controller
{
    private const string TwoFactorStateKey = "twofactor-state";

    private readonly IDashboardAuthorizationService _authService;
    private readonly IStateStore _stateStore;
    private readonly IDashboardRouteAccessor _routeAccessor;
    private readonly ILogger<DashboardAuthController> _logger;

    private readonly string _defaultLocale;
    private readonly DashboardAuthMode _authMode;
    private readonly string? _jwtSecret;
    private readonly string? _jwtPassword;
    private readonly string? _jwtUsername;
    private readonly bool _enableTwoFactor;
    private readonly string? _twoFactorSecret;
    private readonly int _minPasswordLength;

    public DashboardAuthController(
        IDashboardAuthorizationService authService,
        IOptions<DashboardOptions> dashboardOptions,
        IStateStore stateStore,
        IDashboardRouteAccessor routeAccessor,
        ILogger<DashboardAuthController> logger)
    {
        _authService = authService;
        _stateStore = stateStore;
        _routeAccessor = routeAccessor;
        _logger = logger;

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
        ViewBag.DashboardRoutePrefix = _routeAccessor.RoutePrefix;
        ViewBag.AuthMode = _authMode;
        ViewBag.Locale = _defaultLocale == "en-US" ? "en-US" : "zh-CN";
        ViewBag.AllI18nJson = DashboardI18n.AllAsJson(ViewBag.Locale);
        return View();
    }

    // - Login / Logout -

    /// <summary>Login POST - validate credentials and return JWT.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(ApiResponse.Fail("Username and password are required"));

        if (request.Password.Length < _minPasswordLength)
            return BadRequest(ApiResponse.Fail($"Password must be at least {_minPasswordLength} characters"));

        bool valid = _authMode switch
        {
            DashboardAuthMode.CustomJwt =>
                request.Username == _jwtUsername && request.Password == _jwtPassword,
            DashboardAuthMode.DefaultJwt =>
                request.Username == "admin" && request.Password == _jwtPassword,
            _ => false
        };

        if (!valid)
            return Unauthorized(ApiResponse.Fail("Invalid credentials", 401));

        // 2FA verification
        var (twoFactorEnabled, twoFactorSecret) = await GetTwoFactorStateAsync();
        if (twoFactorEnabled && !string.IsNullOrWhiteSpace(twoFactorSecret))
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
                return Ok(ApiResponse.Ok(new { requiresTwoFactor = true }, "Two-factor authentication required"));

            if (!TotpHelper.ValidateCode(twoFactorSecret, request.TwoFactorCode))
                return Unauthorized(ApiResponse.Fail("Invalid two-factor code", 401));
        }

        var token = DashboardJwtHelper.GenerateToken(request.Username, _jwtSecret!);

        Response.Cookies.Append("dashboard_token", token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTime.Now.AddHours(8)
        });

        return Ok(ApiResponse.Ok(new { token }));
    }

    /// <summary>Logout - clear the auth token cookie.</summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("dashboard_token");
        return Ok(ApiResponse.Ok("Logged out successfully"));
    }

    #endregion

    #region Two-Factor Authentication

    /// <summary>Get 2FA status.</summary>
    [HttpGet("api/2fa/status")]
    public async Task<IActionResult> GetTwoFactorStatus()
    {
        var (enabled, _) = await GetTwoFactorStateAsync();
        return Ok(ApiResponse.Ok(new { enabled, minPasswordLength = _minPasswordLength }));
    }

    /// <summary>Generate a new 2FA secret and QR URL.</summary>
    [HttpGet("api/2fa/setup")]
    public IActionResult SetupTwoFactor()
    {
        var secret = TotpHelper.GenerateSecret();
        var issuer = "Gateway Dashboard";
        var account = _jwtUsername ?? "admin";
        var qrUrl = TotpHelper.BuildOtpAuthUri(issuer, account, secret);
        return Ok(ApiResponse.Ok(new { secret, qrUrl }));
    }

    /// <summary>Verify 2FA code and enable 2FA.</summary>
    [HttpPost("api/2fa/verify")]
    public async Task<IActionResult> VerifyTwoFactor([FromBody] JsonElement body)
    {
        var code = body.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
        var secret = body.TryGetProperty("secret", out var secretEl) ? secretEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(secret))
            return BadRequest(ApiResponse.Fail("Code and secret are required"));

        if (!TotpHelper.ValidateCode(secret, code))
            return BadRequest(ApiResponse.Fail("Invalid two-factor code"));

        await SaveTwoFactorStateAsync(true, secret);
        return Ok(ApiResponse.Ok("Two-factor authentication enabled"));
    }

    /// <summary>Disable 2FA.</summary>
    [HttpPost("api/2fa/disable")]
    public async Task<IActionResult> DisableTwoFactor()
    {
        await SaveTwoFactorStateAsync(false, null);
        return Ok(ApiResponse.Ok("Two-factor authentication disabled"));
    }

    // - 2FA State Persistence -

    private async Task<(bool enabled, string? secret)> GetTwoFactorStateAsync()
    {
        try
        {
            var state = await _stateStore.LoadAsync<TwoFactorState>(TwoFactorStateKey);
            if (state != null)
                return (state.Enabled, state.Secret);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load 2FA state");
        }

        return (_enableTwoFactor, _twoFactorSecret);
    }

    private async Task SaveTwoFactorStateAsync(bool enabled, string? secret)
    {
        try
        {
            var state = new TwoFactorState { Enabled = enabled, Secret = secret };
            await _stateStore.SaveAsync(TwoFactorStateKey, state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save 2FA state");
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

        return Ok(ApiResponse.Ok(new
        {
            IsAuthEnabled = isAuthEnabled,
            AuthMode = _authMode.ToString(),
            AuthModeDescription = authModeDescription,
            Locale = _defaultLocale
        }));
    }

    #endregion
}
