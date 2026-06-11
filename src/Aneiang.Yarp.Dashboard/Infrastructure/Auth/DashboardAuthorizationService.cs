using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Alert.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Auth;

/// <summary>
/// Standard dashboard authorization service.
/// Supports all auth modes: None, ApiKey, CustomJwt, DefaultJwt, and custom delegate.
/// </summary>
public sealed class DashboardAuthorizationService : IDashboardAuthorizationService
{
    private readonly DashboardOptions _options;
    private readonly ILogger<DashboardAuthorizationService> _logger;

    /// <summary>
    /// Creates the authorization service.
    /// </summary>
    public DashboardAuthorizationService(IOptions<DashboardOptions> options, ILogger<DashboardAuthorizationService> logger)
    {
        _options = options.Value;
        _logger = logger;
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

        var token = ExtractJwtToken(context);
        if (string.IsNullOrEmpty(token))
            return false;

        var (valid, _) = DashboardJwtHelper.ValidateToken(token, secret);
        if (!valid)
        {
            _logger.LogWarning(
                "Dashboard JWT validation failed for {Path}. RemoteIp: {RemoteIp}",
                context.Request.Path,
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }

        return valid;
    }

    private static string? ExtractJwtToken(HttpContext context)
    {
        // Authorization header (for XHR/API calls)
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader[7..];
        }

        // WebSocket/SignalR clients often pass the token as a query parameter
        var queryToken = context.Request.Query["token"].FirstOrDefault();
        if (!string.IsNullOrEmpty(queryToken))
        {
            return queryToken;
        }

        // dashboard_token cookie (for browser page loads)
        if (context.Request.Cookies.TryGetValue("dashboard_token", out var cookieToken)
            && !string.IsNullOrEmpty(cookieToken))
        {
            return cookieToken;
        }

        return null;
    }
}
