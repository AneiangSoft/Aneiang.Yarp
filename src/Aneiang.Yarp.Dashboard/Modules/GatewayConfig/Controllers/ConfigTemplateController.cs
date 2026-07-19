using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Application;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Controllers;

/// <summary>
/// API controller for configuration templates.
/// </summary>
[ApiController]
public class ConfigTemplateController : ControllerBase
{
    private readonly IConfigTemplateService _service;

    public ConfigTemplateController(IConfigTemplateService service)
    {
        _service = service;
    }

    /// <summary>Get all templates.</summary>
    [HttpGet("api/config/templates")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var templates = await _service.GetAllAsync(ct);
        return Ok(ApiResponse.Ok(templates.Select(t => new
        {
            t.Id,
            t.Name,
            t.Description,
            t.Category,
            t.Difficulty,
            t.Features,
            t.Steps,
            t.Variables
        })));
    }

    /// <summary>Get a single template by ID (includes full config).</summary>
    [HttpGet("api/config/templates/{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var template = await _service.GetAsync(id, ct);
        if (template == null)
            return NotFound(ApiResponse.Fail($"Template '{id}' not found", 404));
        return Ok(ApiResponse.Ok(template));
    }

    /// <summary>Apply a template with variables.</summary>
    [HttpPost("api/config/templates/{id}/apply")]
    public async Task<IActionResult> Apply(string id, [FromBody] Dictionary<string, string> variables, CancellationToken ct)
    {
        var result = await _service.ApplyAsync(id, variables ?? new(), ct);
        if (!result.Success)
            return BadRequest(ApiResponse.Fail(result.Message));
        return Ok(ApiResponse.Ok(result));
    }
}
