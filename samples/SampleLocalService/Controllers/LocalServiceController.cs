using Microsoft.AspNetCore.Mvc;

namespace SampleLocalService.Controllers;

[ApiController]
[Route("/ANEIANG/api/local-service/[action]")]
public class LocalServiceController : ControllerBase
{
    [HttpGet]
    public IActionResult Ping()
    {
        return Ok(new
        {
            code = 200,
            service = "SampleLocalService",
            instance = Environment.MachineName,
            time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    [HttpGet]
    public IActionResult Echo([FromQuery] string? message)
    {
        return Ok(new
        {
            code = 200,
            message = message ?? "hello from local service",
            instance = Environment.MachineName
        });
    }
}
