namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>Controls which built-in middleware is automatically mounted by <see cref="DashboardApplicationBuilderExtensions"/>.</summary>
public sealed class DashboardUseOptions
{
    /// <summary>Override <c>DeploymentOptions.AutoUseMiddleware</c>. Null means use deployment configuration.</summary>
    public bool? AutoUseMiddleware { get; set; }

    /// <summary>Mount deployment endpoint-router and health-check middleware when auto-use is enabled.</summary>
    public bool UseDeploymentMiddleware { get; set; } = true;

    /// <summary>Mount proxy request/response capture middleware when auto-use is enabled.</summary>
    public bool UseProxyRequestCapture { get; set; } = true;

    /// <summary>Mount WAF middleware when auto-use is enabled.</summary>
    public bool UseWaf { get; set; } = true;

    /// <summary>Mount built-in proxy branch middleware when auto-use is enabled.</summary>
    public bool UseBuiltInProxyPipeline { get; set; } = true;

    /// <summary>
    /// Automatically register a default permissive CORS middleware (AllowAnyOrigin/Method/Header).
    /// Set to <c>false</c> and call <c>app.UseCors(...)</c> yourself before <c>app.UseAneiangYarpDashboard(...)</c>
    /// to use a custom CORS policy.
    /// </summary>
    public bool AutoUseCors { get; set; } = true;

    /// <summary>
    /// Automatically register authorization middleware (<c>UseAuthorization()</c>).
    /// Required for YARP routes with non-<c>Anonymous</c> <c>AuthorizationPolicy</c>.
    /// Set to <c>false</c> if you call <c>app.UseAuthorization()</c> yourself.
    /// Call <c>app.UseAuthentication()</c> before <c>UseAneiangYarpDashboard()</c> if your
    /// auth policies require authenticated users.
    /// </summary>
    public bool AutoUseAuthorization { get; set; } = true;
}
