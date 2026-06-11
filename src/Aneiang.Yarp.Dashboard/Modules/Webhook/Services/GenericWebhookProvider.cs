using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Modules.Webhook.Services;

/// <summary>
/// Default generic webhook provider.
/// Sends the raw <see cref="WebhookPayload"/> as JSON with optional HMAC-SHA256 signature
/// in the <c>X-Webhook-Signature</c> header.
/// </summary>
public class GenericWebhookProvider : IWebhookProvider
{
    public string[] SupportedHosts { get; } = [];

    public string PlatformName { get; } = "generic";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public WebhookRequest BuildRequest(string url, WebhookPayload payload, string? secret)
    {
        var json = JsonSerializer.Serialize(payload, _jsonOptions);

        var request = new WebhookRequest
        {
            Url = url,
            Body = json
        };

        if (!string.IsNullOrEmpty(secret))
        {
            var signature = ComputeSignature(json, secret);
            request.Headers["X-Webhook-Signature"] = $"sha256={signature}";
        }

        return request;
    }

    private static string ComputeSignature(string data, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(dataBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
