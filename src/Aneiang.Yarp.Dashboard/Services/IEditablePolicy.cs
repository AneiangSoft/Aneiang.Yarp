using Aneiang.Yarp.Services;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Policy for determining whether clusters and routes are editable.
/// </summary>
public interface IEditablePolicy
{
    /// <summary>
    /// Determines if a cluster is editable.
    /// </summary>
    /// <param name="clusterId">Cluster identifier.</param>
    /// <param name="dynamicConfig">Dynamic configuration service.</param>
    /// <returns>True if editable, false otherwise.</returns>
    bool IsClusterEditable(string clusterId, DynamicYarpConfigService dynamicConfig);

    /// <summary>
    /// Determines if a route is editable.
    /// </summary>
    /// <param name="routeId">Route identifier.</param>
    /// <param name="dynamicConfig">Dynamic configuration service.</param>
    /// <returns>True if editable, false otherwise.</returns>
    bool IsRouteEditable(string routeId, DynamicYarpConfigService dynamicConfig);
}
