using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Storage.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Strategy preset API: save, list, apply, and delete reusable plugin configuration presets.
/// </summary>
[Route("api/presets")]
[ApiController]
public class PresetsController : ControllerBase
{
    private readonly IPluginConfigurationRepository _repository;

    public PresetsController(IPluginConfigurationRepository repository)
    {
        _repository = repository;
    }

    /// <summary>Get all presets, optionally filtered by pluginId.</summary>
    [HttpGet]
    public async Task<IActionResult> GetPresets([FromQuery] string? pluginId, CancellationToken ct)
    {
        var presets = string.IsNullOrWhiteSpace(pluginId)
            ? await _repository.GetPresetsAsync(ct)
            : await _repository.GetPresetsByPluginAsync(pluginId, ct);

        return Ok(new
        {
            code = 200,
            data = presets.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                description = p.Description,
                pluginId = p.PluginId,
                configJson = p.ConfigJson,
                schemaVersion = p.SchemaVersion,
                createdAt = p.CreatedAt,
                updatedAt = p.UpdatedAt
            })
        });
    }

    /// <summary>Get a single preset by id.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPreset(string id, CancellationToken ct)
    {
        var preset = await _repository.GetPresetAsync(id, ct);
        if (preset == null)
            return NotFound(new { code = 404, message = "Preset not found" });

        return Ok(new
        {
            code = 200,
            data = new
            {
                id = preset.Id,
                name = preset.Name,
                description = preset.Description,
                pluginId = preset.PluginId,
                configJson = preset.ConfigJson,
                schemaVersion = preset.SchemaVersion,
                createdAt = preset.CreatedAt,
                updatedAt = preset.UpdatedAt
            }
        });
    }

    /// <summary>Save a new preset or update an existing one.</summary>
    [HttpPost]
    public async Task<IActionResult> SavePreset([FromBody] SavePresetRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
            return BadRequest(new { code = 400, message = "Name is required" });
        if (string.IsNullOrWhiteSpace(request?.PluginId))
            return BadRequest(new { code = 400, message = "PluginId is required" });

        var preset = new PluginConfigPresetEntity
        {
            Id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id!,
            Name = request.Name!,
            Description = request.Description,
            PluginId = request.PluginId!,
            ConfigJson = request.ConfigJson ?? "{}",
            SchemaVersion = request.SchemaVersion > 0 ? request.SchemaVersion : 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.UpsertPresetAsync(preset, ct);
        return Ok(new { code = 200, data = new { id = preset.Id }, message = "Preset saved" });
    }

    /// <summary>Delete a preset by id.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePreset(string id, CancellationToken ct)
    {
        var deleted = await _repository.DeletePresetAsync(id, ct);
        if (!deleted)
            return NotFound(new { code = 404, message = "Preset not found" });

        return Ok(new { code = 200, message = "Preset deleted" });
    }

    /// <summary>Apply a preset to an existing binding.</summary>
    [HttpPost("{id}/apply")]
    public async Task<IActionResult> ApplyPreset(string id, [FromBody] ApplyPresetRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.BindingId))
            return BadRequest(new { code = 400, message = "BindingId is required" });

        var preset = await _repository.GetPresetAsync(id, ct);
        if (preset == null)
            return NotFound(new { code = 404, message = "Preset not found" });

        var binding = await _repository.GetBindingAsync(request.BindingId!, ct);
        if (binding == null)
            return NotFound(new { code = 404, message = "Binding not found" });

        if (!string.Equals(binding.PluginId, preset.PluginId, StringComparison.OrdinalIgnoreCase))
            return Conflict(new { code = 409, message = $"Preset is for plugin '{preset.PluginId}' but binding uses '{binding.PluginId}'" });

        binding.ConfigJson = preset.ConfigJson;
        binding.SchemaVersion = preset.SchemaVersion;
        binding.ConfigVersion += 1;
        binding.UpdatedAt = DateTime.UtcNow;
        await _repository.UpsertBindingAsync(binding, ct);

        return Ok(new { code = 200, message = "Preset applied", data = new { bindingId = binding.Id, configVersion = binding.ConfigVersion } });
    }
}

public class SavePresetRequest
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? PluginId { get; set; }
    public string? ConfigJson { get; set; }
    public int SchemaVersion { get; set; }
}

public class ApplyPresetRequest
{
    public string? BindingId { get; set; }
}
