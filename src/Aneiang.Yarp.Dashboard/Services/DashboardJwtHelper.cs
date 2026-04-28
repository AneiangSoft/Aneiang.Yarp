using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>JWT token generation and validation (zero external dependency) / JWT 令牌生成与验证（零外部依赖）.</summary>
public static class DashboardJwtHelper
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Generate a signed JWT token / 生成签名 JWT 令牌.</summary>
    /// <param name="username">Subject claim / 主体声明.</param>
    /// <param name="secret">HMAC-SHA256 signing key / HMAC-SHA256 签名密钥.</param>
    public static string GenerateToken(string username, string secret)
    {
        var header = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" }, _jsonOptions);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(new
        {
            sub = username,
            iss = "Aneiang.Yarp.Dashboard",
            iat = now,
            exp = now + 28800  // 8 hours
        }, _jsonOptions);

        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        var signingInput = $"{headerB64}.{payloadB64}";

        var signature = new HMACSHA256(Encoding.UTF8.GetBytes(secret))
            .ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        var signatureB64 = Base64UrlEncode(signature);

        return $"{headerB64}.{payloadB64}.{signatureB64}";
    }

    /// <summary>Validate a signed JWT token / 验证签名 JWT 令牌.</summary>
    /// <param name="token">The JWT string / JWT 字符串.</param>
    /// <param name="secret">HMAC-SHA256 signing key / HMAC-SHA256 签名密钥.</param>
    /// <returns>(valid, username) tuple.</returns>
    public static (bool Valid, string? Username) ValidateToken(string token, string secret)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return (false, null);

        var signingInput = $"{parts[0]}.{parts[1]}";
        var expectedSig = ComputeSignature(signingInput, secret);

        if (!ConstantTimeEquals(Base64UrlDecode(parts[2]), expectedSig))
            return (false, null);

        var payloadBytes = Base64UrlDecode(parts[1]);
        using var doc = JsonDocument.Parse(payloadBytes);
        var root = doc.RootElement;

        // Check expiry / 检查过期时间
        if (root.TryGetProperty("exp", out var expEl))
        {
            var expTime = DateTimeOffset.FromUnixTimeSeconds(expEl.GetInt64());
            if (expTime < DateTimeOffset.UtcNow) return (false, null);
        }

        var username = root.TryGetProperty("sub", out var subEl) ? subEl.GetString() : null;
        return (true, username);
    }

    private static byte[] ComputeSignature(string input, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
    }

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var b64 = input.Replace('-', '+').Replace('_', '/');
        var pad = b64.Length % 4;
        if (pad == 2) b64 += "==";
        else if (pad == 3) b64 += "=";
        return Convert.FromBase64String(b64);
    }

    /// <summary>Constant-time comparison to prevent timing attacks / 恒定时间比较防止时序攻击.</summary>
    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
