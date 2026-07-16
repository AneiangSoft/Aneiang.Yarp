namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Lightweight DTO for destination info - eliminates reflection in mapper.
/// </summary>
internal readonly struct DestinationInfoEntry
{
    public string Name { get; init; }
    public string? Address { get; init; }

    public DestinationInfoEntry(string name, string? address)
    {
        Name = name;
        Address = address;
    }
}
