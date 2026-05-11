using Microsoft.AspNetCore.Http;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Interface for dashboard authorization service.
/// Provides a unified authorization check across all auth modes.
/// </summary>
public interface IDashboardAuthorizationService
{
    /// <summary>
    /// Checks if the current request is authorized to access the dashboard.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>True if authorized, false otherwise.</returns>
    Task<bool> IsAuthorizedAsync(HttpContext context);

    /// <summary>
    /// Gets the current authorization mode description.
    /// </summary>
    /// <returns>Human-readable auth mode description.</returns>
    string GetAuthModeDescription();
}
