using System.Diagnostics;
using System.Reflection;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Read-only API exposing runtime deployment information for the Dashboard "Deployment" page.
/// </summary>
[ApiController]
[Route("api/deployment")]
public class DeploymentInfoController : ControllerBase
{
    private readonly DeploymentOptions _options;
    private readonly DeploymentRestartState _restartState;
    private readonly ILogger<DeploymentInfoController> _logger;

    private static readonly DateTime _processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    private static readonly string _version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

    public DeploymentInfoController(
        IOptions<DeploymentOptions> options,
        DeploymentRestartState restartState,
        ILogger<DeploymentInfoController> logger)
    {
        _options = options.Value;
        _restartState = restartState;
        _logger = logger;
    }

    /// <summary>Get current deployment summary: mode, endpoints, and health-check settings.</summary>
    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        var healthAuth = new List<string>();
        if (!string.IsNullOrEmpty(_options.HealthCheck.Token)) healthAuth.Add("Token");
        if (_options.HealthCheck.AllowedIps.Count > 0) healthAuth.Add("IP Whitelist");

        var healthChecks = new List<string>();
        if (_options.HealthCheck.CheckDatabase) healthChecks.Add("Database");
        if (_options.HealthCheck.CheckConfigLoaded) healthChecks.Add("Config");

        return Ok(new
        {
            mode = _options.Mode.ToString(),
            processStart = _processStart,
            uptimeSeconds = (DateTime.Now - _processStart).TotalSeconds,
            version = _version,
            environment = env,
            restartRequired = _restartState.IsRestartRequired,
            restartReasons = _restartState.GetReasons(),
            endpoints = _options.ResolvedEndpoints.Select(e => new
            {
                name = e.EndpointName,
                port = e.Port,
                address = e.IpAddress,
                role = e.Role,
                isPublic = e.IsPubliclyBound
            }),
            healthCheck = new
            {
                enabled = _options.HealthCheck.Enabled,
                path = _options.HealthCheck.Path,
                readyPath = _options.HealthCheck.ReadyPath,
                livePath = _options.HealthCheck.LivePath,
                hasToken = !string.IsNullOrEmpty(_options.HealthCheck.Token),
                ipWhitelist = _options.HealthCheck.AllowedIps,
                authentication = healthAuth,
                checks = healthChecks
            }
        });
    }
}
