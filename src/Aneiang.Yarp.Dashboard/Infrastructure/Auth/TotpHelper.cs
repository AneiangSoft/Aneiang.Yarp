using System.Security.Cryptography;
using System.Text;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Auth;

/// <summary>
/// TOTP (Time-based One-Time Password) helper for 2FA.
/// Implements RFC 6238 with SHA1, 30-second period, 6 digits.
/// </summary>
public static class TotpHelper
{
    private const int PeriodSeconds = 30;
    private const int Digits = 6;

    /// <summary>Generate a TOTP code from a base32-encoded secret key.</summary>
    public static string GenerateCode(string base32Secret, DateTime? timestamp = null)
    {
        var secret = Base32Decode(base32Secret);
        var counter = (timestamp ?? DateTime.UtcNow).ToUnixTimeSeconds() / PeriodSeconds;
        return GenerateCode(secret, counter);
    }

    /// <summary>Validate a TOTP code with a ±1 period window.</summary>
    public static bool ValidateCode(string base32Secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != Digits)
            return false;

        var now = DateTime.UtcNow.ToUnixTimeSeconds();
        for (var offset = -1; offset <= 1; offset++)
        {
            var counter = (now / PeriodSeconds) + offset;
            var expected = GenerateCode(Base32Decode(base32Secret), counter);
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected.PadLeft(Digits, '0')),
                Encoding.ASCII.GetBytes(code.PadLeft(Digits, '0'))))
                return true;
        }
        return false;
    }

    /// <summary>Generate a new random base32 secret (32 chars = 20 bytes).</summary>
    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return Base32Encode(bytes);
    }

    /// <summary>Build an otpauth:// URI for QR code generation.</summary>
    public static string BuildOtpAuthUri(string issuer, string account, string base32Secret)
    {
        return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";
    }

    private static string GenerateCode(byte[] secret, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) |
                     ((hash[offset + 1] & 0xFF) << 16) |
                     ((hash[offset + 2] & 0xFF) << 8) |
                     (hash[offset + 3] & 0xFF);
        var code = binary % (int)Math.Pow(10, Digits);
        return code.ToString().PadLeft(Digits, '0');
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        base32 = base32.ToUpperInvariant().TrimEnd('=');
        var bytes = new byte[base32.Length * 5 / 8];
        var buffer = 0;
        var bitsLeft = 0;
        var index = 0;

        foreach (var c in base32)
        {
            var val = alphabet.IndexOf(c);
            if (val < 0) continue;
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes[index++] = (byte)(buffer >> (bitsLeft - 8));
                bitsLeft -= 8;
            }
        }

        return bytes;
    }

    private static string Base32Encode(byte[] bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new StringBuilder();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                result.Append(alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
            result.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);

        return result.ToString();
    }

    private static long ToUnixTimeSeconds(this DateTime dt)
        => new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
}
