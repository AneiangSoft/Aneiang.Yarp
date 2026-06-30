using Aneiang.Yarp.Services;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Services;

/// <summary>
/// Default implementation of editable policy.
/// Rules:
/// - Static configuration source (source == "config") is not editable by default.
/// - Dynamic configuration source is editable.
/// - Reserved extension points for environment/namespace/tag rules.
/// </summary>
internal sealed class DashboardEditablePolicy : IEditablePolicy
{
    /// <inheritdoc />
    public bool IsClusterEditable(string clusterId, DynamicYarpConfigService dynamicConfig)
    {
        var dynConfig = dynamicConfig.GetDynamicConfig();
        var dynCluster = dynConfig?.Clusters.FirstOrDefault(dc =>
            string.Equals(dc.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

        if (dynCluster != null)
        {
            // Static config from appsettings.json is not editable
            return dynCluster.Source != "config";
        }

        // Unknown source is editable by default
        return true;
    }

    /// <inheritdoc />
    public bool IsRouteEditable(string routeId, DynamicYarpConfigService dynamicConfig)
    {
        var dynConfig = dynamicConfig.GetDynamicConfig();
        var dynRoute = dynConfig?.Routes.FirstOrDefault(dr =>
            string.Equals(dr.Config.RouteId, routeId, StringComparison.OrdinalIgnoreCase));

        if (dynRoute != null)
        {
            // Static config from appsettings.json is not editable
            return dynRoute.Source != "config";
        }

        // Unknown source is editable by default
        return true;
    }
}
