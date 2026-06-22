using Aneiang.Yarp.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Services;

internal sealed class GatewayControlPlaneOptions
{
    public bool EnableRegistration { get; set; }
}

internal sealed class GatewayControlPlaneSecurityValidator : IHostedService
{
    private readonly GatewayControlPlaneOptions _controlPlaneOptions;
    private readonly GatewayApiAuthOptions _authOptions;
    private readonly ControlPlaneSecurityOptions _securityOptions;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GatewayControlPlaneSecurityValidator> _logger;

    public GatewayControlPlaneSecurityValidator(
        GatewayControlPlaneOptions controlPlaneOptions,
        IOptions<GatewayApiAuthOptions> authOptions,
        IOptions<ControlPlaneSecurityOptions> securityOptions,
        IHostEnvironment environment,
        ILogger<GatewayControlPlaneSecurityValidator> logger)
    {
        _controlPlaneOptions = controlPlaneOptions;
        _authOptions = authOptions.Value;
        _securityOptions = securityOptions.Value;
        _environment = environment;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_controlPlaneOptions.EnableRegistration) return Task.CompletedTask;

        if (_environment.IsProduction() && _securityOptions.RequireAuthInProduction && _authOptions.Mode == GatewayApiAuthMode.None)
        {
            throw new InvalidOperationException("Gateway registration API is enabled without authentication in Production. Configure Gateway:Security:ControlPlane or call AddAneiangYarp(enableRegistration: false).");
        }

        if (_authOptions.Mode == GatewayApiAuthMode.None)
        {
            _logger.LogWarning("Gateway registration API is enabled without authentication. This is unsafe outside local development.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
