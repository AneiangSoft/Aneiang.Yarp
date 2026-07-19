using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Modules.Features;

/// <summary>
/// Provides catalog of YARP gateway features for the Feature Catalog page.
/// </summary>
public interface IFeatureCatalogService
{
    /// <summary>Get all features.</summary>
    Task<List<FeatureInfo>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Get a single feature by ID.</summary>
    Task<FeatureInfo?> GetAsync(string featureId, CancellationToken ct = default);
}

/// <summary>Metadata for a single gateway feature.</summary>
public class FeatureInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Category { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> KeyPoints { get; set; } = [];
    public string ConfigLocation { get; set; } = "";
    public string ExampleConfig { get; set; } = "";
    public string? DocUrl { get; set; }
    public string ConfigPageUrl { get; set; } = "";
    public bool IsPlugin { get; set; }
    public string? PluginId { get; set; }
    public List<FeatureOption> Options { get; set; } = [];
}

/// <summary>An option/value for a feature.</summary>
public class FeatureOption
{
    public string Value { get; set; } = "";
    public string Description { get; set; } = "";
}
