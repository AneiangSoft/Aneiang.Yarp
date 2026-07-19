using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Application;

/// <summary>
/// Provides configuration templates that can be applied with one click.
/// </summary>
public interface IConfigTemplateService
{
    /// <summary>Get all templates.</summary>
    Task<List<ConfigTemplate>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Get a single template by ID.</summary>
    Task<ConfigTemplate?> GetAsync(string templateId, CancellationToken ct = default);

    /// <summary>Apply a template with the given variables.</summary>
    Task<ApplyResult> ApplyAsync(string templateId, Dictionary<string, string> variables, CancellationToken ct = default);
}

/// <summary>A configuration template.</summary>
public class ConfigTemplate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public List<string> Features { get; set; } = [];
    public JsonElement Config { get; set; }
    public List<string> Steps { get; set; } = [];
    public List<ConfigVariable> Variables { get; set; } = [];
}

/// <summary>A variable that needs to be filled in by the user.</summary>
public class ConfigVariable
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string DefaultValue { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Required { get; set; }
}

/// <summary>Result of applying a template.</summary>
public class ApplyResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int ImportedRoutes { get; set; }
    public int ImportedClusters { get; set; }
}
