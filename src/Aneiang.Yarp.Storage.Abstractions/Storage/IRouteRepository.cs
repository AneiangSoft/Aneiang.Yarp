namespace Aneiang.Yarp.Storage;

public interface IRouteRepository
{
    Task<RouteEntity?> GetRouteAsync(string routeId, CancellationToken ct = default);
    Task<IReadOnlyList<RouteEntity>> GetAllRoutesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RouteEntity>> GetRoutesByClusterAsync(string clusterId, CancellationToken ct = default);
    Task SaveRouteAsync(RouteEntity route, CancellationToken ct = default);
    Task SaveRoutesAsync(IEnumerable<RouteEntity> routes, CancellationToken ct = default);
    Task DeleteRouteAsync(string routeId, CancellationToken ct = default);
    Task DeleteRoutesByClusterAsync(string clusterId, CancellationToken ct = default);
}
