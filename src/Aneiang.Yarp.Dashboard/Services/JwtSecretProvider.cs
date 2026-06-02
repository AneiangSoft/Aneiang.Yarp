using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Manages JWT signing key lifecycle: generates, persists, and retrieves the secret.
/// Ensures the signing key survives application restarts so issued tokens remain valid.
/// </summary>
public sealed class JwtSecretProvider
{
    private readonly string _filePath;
    private readonly ILogger<JwtSecretProvider> _logger;
    private string? _cachedSecret;

    public JwtSecretProvider(IWebHostEnvironment env, ILogger<JwtSecretProvider> logger)
    {
        _logger = logger;
        var dataDir = Path.Combine(env.ContentRootPath, "Data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, ".jwt-secret");
    }

    /// <summary>
    /// Returns the JWT signing secret.
    /// Priority: configured value → persisted file → auto-generated and persisted.
    /// </summary>
    /// <param name="configuredSecret">Secret from configuration (null means "auto-generate if needed").</param>
    /// <returns>The active JWT signing secret.</returns>
    public string GetSecret(string? configuredSecret)
    {
        if (_cachedSecret != null)
            return _cachedSecret;

        if (!string.IsNullOrWhiteSpace(configuredSecret))
        {
            _cachedSecret = configuredSecret;
            _logger.LogInformation("Using JWT secret from configuration");
            return _cachedSecret;
        }

        _cachedSecret = LoadOrCreateSecret();
        return _cachedSecret;
    }

    private string LoadOrCreateSecret()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("secret", out var secretProp))
                {
                    var secret = secretProp.GetString();
                    if (!string.IsNullOrWhiteSpace(secret))
                    {
                        _logger.LogInformation("JWT secret loaded from {File}", _filePath);
                        return secret;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read JWT secret file, generating a new one");
            }
        }

        var newSecret = GenerateSecret();
        PersistSecret(newSecret);
        _logger.LogWarning(
            "JWT secret auto-generated and persisted to {File}. " +
            "All existing tokens will be invalidated after restart. " +
            "Set Gateway:Dashboard:JwtSecret explicitly in configuration for production.",
            _filePath);
        return newSecret;
    }

    private void PersistSecret(string secret)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { secret, generatedAt = DateTime.UtcNow });
            File.WriteAllText(_filePath, payload);
            _logger.LogDebug("JWT secret persisted to {File}", _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist JWT secret to {File} — tokens will not survive restart", _filePath);
        }
    }

    private static string GenerateSecret()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
