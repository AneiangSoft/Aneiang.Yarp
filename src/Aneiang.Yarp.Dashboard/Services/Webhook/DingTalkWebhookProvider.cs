using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Services.Webhook;

/// <summary>
/// DingTalk (钉钉) custom robot webhook provider.
/// Converts the generic payload into DingTalk's <c>text</c> message format
/// and handles DingTalk-specific HMAC-SHA256 signing (timestamp + "\n" + secret).
/// </summary>
/// <remarks>
/// DingTalk webhook URL format: https://oapi.dingtalk.com/robot/send?access_token=XXX
/// With signing, appends &amp;timestamp=XXX&amp;sign=XXX to the URL.
///
/// DingTalk signature algorithm:
///   stringToSign = timestamp + "\n" + secret
///   sign = Base64(HmacSHA256(secret, stringToSign))
///   sign = UrlEncode(sign)
/// </remarks>
public class DingTalkWebhookProvider : IWebhookProvider
{
    public string[] SupportedHosts { get; } = ["oapi.dingtalk.com"];

    public string PlatformName { get; } = "dingtalk";

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    public WebhookRequest BuildRequest(string url, WebhookPayload payload, string? secret)
    {
        // Build DingTalk text message format
        var content = FormatContent(payload);
        var body = JsonSerializer.Serialize(new
        {
            msgtype = "text",
            text = new { content }
        }, _jsonOptions);

        var request = new WebhookRequest
        {
            Url = url,
            Body = body
        };

        // DingTalk signing: append timestamp & sign to URL
        if (!string.IsNullOrEmpty(secret))
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var stringToSign = $"{timestamp}\n{secret}";
            var sign = ComputeDingTalkSign(stringToSign, secret);
            var separator = url.Contains('?') ? "&" : "?";
            request.Url = $"{url}{separator}timestamp={timestamp}&sign={sign}";
        }

        return request;
    }

    private static string FormatContent(WebhookPayload payload)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[YARP Gateway 配置变更通知]");
        sb.AppendLine($"事件: {payload.EventType}");
        sb.AppendLine($"目标: {payload.Target}");
        sb.Append($"时间: {payload.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");

        if (!string.IsNullOrEmpty(payload.Operator))
            sb.Append($"\n操作人: {payload.Operator}");

        if (payload.Details != null)
            sb.Append($"\n详情: {JsonSerializer.Serialize(payload.Details, _jsonOptions)}");

        return sb.ToString();
    }

    /// <summary>
    /// Compute DingTalk signature: Base64(HmacSHA256(secret, stringToSign)) → UrlEncode.
    /// </summary>
    private static string ComputeDingTalkSign(string stringToSign, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var signData = Encoding.UTF8.GetBytes(stringToSign);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(signData);
        return Uri.EscapeDataString(Convert.ToBase64String(hash));
    }
}
