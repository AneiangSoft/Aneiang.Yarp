using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Service to automatically configure Kestrel to listen on 0.0.0.0 for cross-machine access.
/// Detects localhost binding and auto-configures launchSettings.json or suggests manual configuration.
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
    /// Check if service needs to listen on 0.0.0.0 and attempt auto-configuration.
    /// </summary>
    /// <param name="port">The port the service is running on.</param>
    /// <returns>True if already listening on 0.0.0.0 or successfully auto-configured.</returns>
    public bool EnsureListeningOnAny(int port)
    {
        // Check current listening status
        if (IsListeningOnAnyAddress(port))
        {
            _logger.LogDebug("Service already listening on 0.0.0.0:{Port}", port);
            return true;
        }

        _logger.LogWarning(
            "Service is listening on localhost only (port {Port}). " +
            "Attempting to auto-configure for cross-machine access...", 
            port);

        bool anyModified = false;

        // Try to auto-configure launchSettings.json
        if (TryConfigureLaunchSettings(port))
        {
            _logger.LogInformation("Successfully updated launchSettings.json");
            anyModified = true;
        }

        // Try to auto-configure appsettings.json
        if (TryConfigureAppSettings(port))
        {
            _logger.LogInformation("Successfully updated appsettings.json");
            anyModified = true;
        }

        if (anyModified)
        {
            _logger.LogInformation(
                "Configuration updated. Please restart the service for changes to take effect. " +
                "Service will register with LAN IP, but cross-machine access requires restart.");
            return false; // Need restart
        }

        // Fallback: log instructions
        _logger.LogWarning(
            "Could not auto-configure. Please manually configure Kestrel to listen on 0.0.0.0:\n" +
            "  Option 1 - appsettings.json:\n" +
            "    \"Urls\": \"http://0.0.0.0:{0}\"\n" +
            "  Option 2 - launchSettings.json:\n" +
            "    \"applicationUrl\": \"http://0.0.0.0:{1}\"\n" +
            "  Option 3 - Program.cs:\n" +
            "    builder.WebHost.UseUrls(\"http://0.0.0.0:{2}\")\n" +
            "  Option 4 - Environment variable:\n" +
            "    ASPNETCORE_URLS=http://0.0.0.0:{3}",
            port, port, port, port);

        return false;
    }

    /// <summary>
    /// Check if any TCP listener is on 0.0.0.0:port.
    /// </summary>
    private static bool IsListeningOnAnyAddress(int port)
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

    /// <summary>
    /// Try to auto-configure launchSettings.json to listen on 0.0.0.0.
    /// </summary>
    private bool TryConfigureLaunchSettings(int port)
    {
        try
        {
            // Find launchSettings.json
            var launchSettingsPath = FindLaunchSettingsFile();
            if (string.IsNullOrEmpty(launchSettingsPath))
            {
                _logger.LogDebug("launchSettings.json not found");
                return false;
            }

            var json = File.ReadAllText(launchSettingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check if profiles exist
            if (!root.TryGetProperty("profiles", out var profiles))
            {
                _logger.LogDebug("No profiles section in launchSettings.json");
                return false;
            }

            bool modified = false;
            var modifiedJson = json;

            foreach (var profile in profiles.EnumerateObject())
            {
                if (profile.Value.TryGetProperty("applicationUrl", out var appUrlProp))
                {
                    var appUrl = appUrlProp.GetString();
                    if (!string.IsNullOrEmpty(appUrl) && 
                        appUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
                    {
                        // Replace localhost with 0.0.0.0
                        var newUrl = appUrl.Replace("localhost", "0.0.0.0", StringComparison.OrdinalIgnoreCase);
                        
                        // Replace in the original JSON string
                        modifiedJson = modifiedJson.Replace(
                            $"\"applicationUrl\": \"{appUrl}\"",
                            $"\"applicationUrl\": \"{newUrl}\"");
                        
                        modified = true;

                        _logger.LogInformation(
                            "Updated applicationUrl in profile '{Profile}': {OldUrl} -> {NewUrl}",
                            profile.Name, appUrl, newUrl);
                    }
                }
            }

            if (modified)
            {
                File.WriteAllText(launchSettingsPath, modifiedJson);
                
                _logger.LogInformation(
                    "Successfully updated launchSettings.json at {Path}", 
                    launchSettingsPath);
                
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-configure launchSettings.json");
            return false;
        }
    }

    /// <summary>
    /// Try to auto-configure appsettings.json to listen on 0.0.0.0.
    /// </summary>
    private bool TryConfigureAppSettings(int port)
    {
        try
        {
            // Find appsettings.json
            var appSettingsPath = FindAppSettingsFile();
            if (string.IsNullOrEmpty(appSettingsPath))
            {
                _logger.LogDebug("appsettings.json not found");
                return false;
            }

            var json = File.ReadAllText(appSettingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check if Urls property exists
            if (!root.TryGetProperty("Urls", out var urlsProp))
            {
                _logger.LogDebug("No Urls property in appsettings.json");
                return false;
            }

            var urls = urlsProp.GetString();
            if (string.IsNullOrEmpty(urls) || 
                !urls.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Urls does not contain localhost");
                return false;
            }

            // Replace localhost with 0.0.0.0
            var newUrls = urls.Replace("localhost", "0.0.0.0", StringComparison.OrdinalIgnoreCase);
            
            // Replace in the original JSON string
            var modifiedJson = json.Replace(
                $"\"Urls\": \"{urls}\"",
                $"\"Urls\": \"{newUrls}\"");

            File.WriteAllText(appSettingsPath, modifiedJson);

            _logger.LogInformation(
                "Updated Urls in appsettings.json: {OldUrl} -> {NewUrl}",
                urls, newUrls);
            
            _logger.LogInformation(
                "Successfully updated appsettings.json at {Path}", 
                appSettingsPath);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-configure appsettings.json");
            return false;
        }
    }

    /// <summary>
    /// Find launchSettings.json file (searches in Properties/ folder relative to current directory).
    /// </summary>
    private static string? FindLaunchSettingsFile()
    {
        var searchPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "Properties", "launchSettings.json"),
            Path.Combine(AppContext.BaseDirectory, "Properties", "launchSettings.json"),
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Find appsettings.json file (searches in current directory).
    /// </summary>
    private static string? FindAppSettingsFile()
    {
        var searchPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
