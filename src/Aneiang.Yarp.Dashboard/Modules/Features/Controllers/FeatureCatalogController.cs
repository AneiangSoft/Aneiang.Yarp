using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Modules.Features;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Features.Controllers;

/// <summary>
/// API controller for the feature catalog.
/// </summary>
[ApiController]
public class FeatureCatalogController : ControllerBase
{
    private readonly IFeatureCatalogService _service;

    public FeatureCatalogController(IFeatureCatalogService service)
    {
        _service = service;
    }

    /// <summary>Get all features.</summary>
    [HttpGet("api/features")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var features = await _service.GetAllAsync(ct);
        return Ok(ApiResponse.Ok(features));
    }

    /// <summary>Get a single feature by ID.</summary>
    [HttpGet("api/features/{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var feature = await _service.GetAsync(id, ct);
        if (feature == null)
            return NotFound(ApiResponse.Fail($"Feature '{id}' not found", 404));
        return Ok(ApiResponse.Ok(feature));
    }
}
