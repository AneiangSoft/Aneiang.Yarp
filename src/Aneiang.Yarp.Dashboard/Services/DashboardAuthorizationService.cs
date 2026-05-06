using Aneiang.Yarp.Dashboard.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Standard dashboard authorization service.
/// Supports all auth modes: None, ApiKey, CustomJwt, DefaultJwt, and custom delegate.
/// </summary>
public sealed class DashboardAuthorizationService : IDashboardAuthorizationService
{
    private readonly DashboardOptions _options;

    /// <summary>
    /// Creates the authorization service.
    /// </summary>
    public DashboardAuthorizationService(IOptions<DashboardOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<bool> IsAuthorizedAsync(HttpContext context)
    {
        // Priority 1: Custom delegate (highest)
        if (_options.AuthorizeRequest != null)
        {
            return await _options.AuthorizeRequest(context);
        }

        // Priority 2: API Key
        if (_options.AuthMode == DashboardAuthMode.ApiKey && !string.IsNullOrEmpty(_options.ApiKey))
        {
            return CheckApiKey(context);
        }

        // Priority 3: JWT (DefaultJwt / CustomJwt)
        if (_options.AuthMode is DashboardAuthMode.CustomJwt or DashboardAuthMode.DefaultJwt)
        {
            return CheckJwt(context);
        }

        // AuthMode.None - no authorization required
        return true;
    }

    /// <inheritdoc />
    public string GetAuthModeDescription()
    {
        if (_options.AuthorizeRequest != null)
            return "Custom Delegate";

        return _options.AuthMode switch
        {
            DashboardAuthMode.None => "None",
            DashboardAuthMode.ApiKey => "API Key",
            DashboardAuthMode.CustomJwt => $"JWT (User: {_options.JwtUsername ?? "N/A"})",
            DashboardAuthMode.DefaultJwt => "JWT (Admin)",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Checks API key authorization.
    /// </summary>
    private bool CheckApiKey(HttpContext context)
    {
        var apiKey = _options.ApiKey!;
        var headerName = _options.ApiKeyHeaderName;

        // Check header
        if (context.Request.Headers.TryGetValue(headerName, out var hv) && 
            hv.Any(v => v == apiKey))
        {
            return true;
        }

        // Check query parameter
        if (context.Request.Query.TryGetValue("api-key", out var qv) && 
            qv.Any(v => v == apiKey))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks JWT authorization.
    /// </summary>
    private bool CheckJwt(HttpContext context)
    {
        var secret = _options.JwtSecret;
        if (string.IsNullOrEmpty(secret))
            return false;

        // Authorization header (for XHR/API calls)
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
        {
            var (valid, _) = DashboardJwtHelper.ValidateToken(authHeader[7..], secret);
            if (valid) return true;
        }

        // dashboard_token cookie (for browser page loads)
        if (context.Request.Cookies.TryGetValue("dashboard_token", out var cookieToken)
            && !string.IsNullOrEmpty(cookieToken))
        {
            var (valid, _) = DashboardJwtHelper.ValidateToken(cookieToken, secret);
            return valid;
        }

        return false;
    }
}
