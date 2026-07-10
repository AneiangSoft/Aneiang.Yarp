using System.Security.Cryptography;
using System.Text;

namespace Aneiang.Yarp.Storage;

/// <summary>
/// Deterministic UID generator based on a prefix + key combination.
/// Uses SHA-256 truncated to 16 bytes (128 bits) for stable, collision-resistant IDs.
/// </summary>
public static class StableUid
{
    /// <summary>
    /// Generate a stable 32-character hex UID from a prefix and key.
    /// The same (prefix, key) pair always produces the same UID.
    /// </summary>
    public static string FromKey(string prefix, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(prefix + ":" + key));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }
}
