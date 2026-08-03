using System.Text.Json;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Storage;

/// <summary>
/// Dashboard-specific entity mapping extensions for ConfigSnapshot types.
/// These types live in the Dashboard assembly and cannot be mapped from the core library.
/// Route/Cluster/Destination/Audit mappings are in <see cref="ConfigEntityMapper"/>.
/// </summary>
internal static class DashboardEntityMapper
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region ConfigSnapshot

    public static ConfigHistoryEntity ToEntity(this ConfigSnapshot snapshot, string? createdBy = null) => new()
    {
        VersionId = snapshot.VersionId,
        Description = snapshot.Description,
        ClientIp = snapshot.ClientIp,
        ConfigData = snapshot.Config.ToString(),
        CreatedBy = createdBy ?? "system",
        CreatedAt = snapshot.Timestamp
    };

    public static ConfigSnapshot ToConfigSnapshot(this ConfigHistoryEntity entity) => new()
    {
        VersionId = entity.VersionId,
        Description = entity.Description,
        ClientIp = entity.ClientIp,
        Config = JsonSerializer.Deserialize<JsonElement>(entity.ConfigData),
        Timestamp = entity.CreatedAt
    };

    public static List<ConfigSnapshot> ToConfigSnapshots(this IEnumerable<ConfigHistoryEntity> entities)
        => entities.Select(e => e.ToConfigSnapshot()).ToList();

    #endregion
}
