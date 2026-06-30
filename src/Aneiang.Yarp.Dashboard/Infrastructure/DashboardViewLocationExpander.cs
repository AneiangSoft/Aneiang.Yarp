using Microsoft.AspNetCore.Mvc.Razor;

namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// Makes controllers whose name doesn't match the &quot;Dashboard&quot; folder
/// (e.g. <c>DashboardAuthController</c>, <c>DashboardPagesController</c>)
/// also search <c>Views/Dashboard/</c> for their views.
/// </summary>
internal sealed class DashboardViewLocationExpander : IViewLocationExpander
{
    public void PopulateValues(ViewLocationExpanderContext context) { }

    public IEnumerable<string> ExpandViewLocations(
        ViewLocationExpanderContext context,
        IEnumerable<string> viewLocations)
    {
        // Inject the "views in Dashboard folder" location BEFORE the existing locations
        // so that Views/Dashboard/{0}.cshtml is checked before the controller-name folder.
        if (context.ControllerName is "DashboardAuth" or "DashboardPages")
        {
            yield return "/Views/Dashboard/{0}.cshtml";
        }

        foreach (var location in viewLocations)
            yield return location;
    }
}
