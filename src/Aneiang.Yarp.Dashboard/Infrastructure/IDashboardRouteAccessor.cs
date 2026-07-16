namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// Provides access to the resolved dashboard route prefix.
/// Replaces the previous static mutable RoutePrefix property on DashboardPagesController.
/// to support multi-instance deployments and enable testability.
/// </summary>
public interface IDashboardRouteAccessor
{
    /// <summary>Resolved route prefix (e.g. "apigateway").</summary>
    string RoutePrefix { get; }
}

/// <summary>
/// Default mutable implementation of <see cref="IDashboardRouteAccessor"/>.
/// Written once during MVC options setup, read-only afterwards.
/// </summary>
public class DashboardRouteAccessor : IDashboardRouteAccessor
{
    public string RoutePrefix { get; set; } = "apigateway";
}
