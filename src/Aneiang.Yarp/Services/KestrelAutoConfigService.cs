using System.Net;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Service to check if Kestrel is listening on 0.0.0.0 for cross-machine access.
/// Provides warnings and suggestions if the service is only listening on localhost.
/// For automatic configuration, use builder.UseYarpKestrelAutoConfig() extension method.
/// </summary>
public class KestrelAutoConfigService
{
    private readonly ILogger<KestrelAutoConfigService> _logger;

    /// <summary>
    /// Initializes a new instance of KestrelAutoConfigService.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public KestrelAutoConfigService(ILogger<KestrelAutoConfigService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Check if service is listening on 0.0.0.0 and log warnings if not.
    /// Does NOT modify any files. For automatic configuration, use UseYarpKestrelAutoConfig() extension method.
    /// </summary>
    /// <param name="port">The port the service is running on.</param>
    /// <returns>True if already listening on 0.0.0.0.</returns>
    public bool EnsureListeningOnAny(int port)
    {
        // Check current listening status
        if (IsListeningOnAnyAddress(port))
        {
            _logger.LogDebug("Service already listening on 0.0.0.0:{Port}", port);
            return true;
        }


        // Not listening on 0.0.0.0, log warning with suggestions
        _logger.LogWarning(
            "Service is listening on localhost only (port {Port}), other machines cannot access! " +
            "To enable cross-machine access, configure Kestrel to listen on 0.0.0.0:\n" +
            "  Option 1 (Recommended): Add in Program.cs before Build():\n" +
            "    builder.UseYarpKestrelAutoConfig();\n" +
            "  Option 2 - appsettings.json:\n" +
            "    \"Urls\": \"http://0.0.0.0:{Port1}\"\n" +
            "  Option 3 - launchSettings.json:\n" +
            "    \"applicationUrl\": \"http://0.0.0.0:{Port2}\"\n" +
            "  Option 4 - Program.cs:\n" +
            "    builder.WebHost.UseUrls(\"http://0.0.0.0:{Port3}\")\n" +
            "  Option 5 - Environment variable:\n" +
            "    ASPNETCORE_URLS=http://0.0.0.0:{Port4}",
            port, port, port, port, port);

        return false;
    }

    /// <summary>
    /// Check if any TCP listener is on 0.0.0.0:port.
    /// </summary>
    public static bool IsListeningOnAnyAddress(int port)
    {
        try
        {
            var listeners = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Where(l => l.Port == port);

            foreach (var listener in listeners)
            {
                if (listener.Address.Equals(IPAddress.Any) ||
                    listener.Address.Equals(IPAddress.IPv6Any))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return true; // Optimistic: assume it's listening
        }
    }
}
