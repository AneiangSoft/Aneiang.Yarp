using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Helpers;

/// <summary>
/// Zero-allocation IP matching utilities (exact, CIDR, wildcard).
/// Extracted from <see cref="Middleware.WafMiddleware"/> for reuse.
/// 
/// Memory optimization (v2.4): WildcardRegexCache Clear() method added
/// to allow rebuilding the cache when WAF IP rules change, preventing
/// stale Regex objects from accumulating indefinitely.
/// </summary>
public static class IpMatcher
{
    /// <summary>Ultra-tight timeout prevents catastrophic backtracking.</summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(5);

    private static readonly ConcurrentDictionary<string, Regex> WildcardRegexCache = new();

    /// <summary>
    /// Clear the wildcard regex cache. Should be called when WAF IP rules
    /// are updated (e.g., IpWhitelist/IpBlacklist configuration changes)
    /// to prevent stale patterns from accumulating in memory.
    /// </summary>
    public static void ClearWildcardRegexCache()
    {
        WildcardRegexCache.Clear();
    }

    /// <summary>
    /// Matches a client IP against a pattern (exact, CIDR, or wildcard).
    /// Wildcard patterns use a cached compiled <see cref="Regex"/>.
    /// </summary>
    public static bool Matches(string pattern, string clientIp)
    {
        if (pattern.Contains('/'))
            return IsInCidrRange(pattern, clientIp);

        if (pattern.Contains('*'))
        {
            var cachedRegex = WildcardRegexCache.GetOrAdd(pattern, p =>
                new Regex(
                    "^" + Regex.Escape(p).Replace("\\*", ".*") + "$",
                    RegexOptions.IgnoreCase,
                    RegexTimeout));
            return cachedRegex.IsMatch(clientIp);
        }

        return string.Equals(pattern, clientIp, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if client IP is within a CIDR range using zero-allocation bit operations.
    /// Supports both IPv4 (uint bitmask) and IPv6 (full-byte prefix matching).
    /// </summary>
    public static bool IsInCidrRange(string cidr, string clientIp)
    {
        var slashIdx = cidr.IndexOf('/');
        if (slashIdx < 0) return false;

        if (!IPAddress.TryParse(cidr.AsSpan(0, slashIdx), out var baseIp))
            return false;
        if (!IPAddress.TryParse(clientIp, out var clientIpAddr))
            return false;

        var baseBytes = baseIp.GetAddressBytes();
        var clientBytes = clientIpAddr.GetAddressBytes();
        if (baseBytes.Length != clientBytes.Length)
            return false;

        if (!int.TryParse(cidr.AsSpan(slashIdx + 1), out var maskBits))
            return false;

        if (baseBytes.Length == 4)
        {
            // IPv4: zero-allocation bit mask using inline bit operations
            uint baseUint = ((uint)baseBytes[0] << 24) | ((uint)baseBytes[1] << 16) | ((uint)baseBytes[2] << 8) | baseBytes[3];
            uint clientUint = ((uint)clientBytes[0] << 24) | ((uint)clientBytes[1] << 16) | ((uint)clientBytes[2] << 8) | clientBytes[3];
            uint mask = maskBits == 0 ? 0u : (~0u << (32 - maskBits));
            return (baseUint & mask) == (clientUint & mask);
        }

        if (baseBytes.Length == 16)
        {
            var fullBytes = maskBits / 8;
            var remainingBits = maskBits % 8;
            for (int i = 0; i < fullBytes; i++)
            {
                if (baseBytes[i] != clientBytes[i])
                    return false;
            }
            if (remainingBits > 0 && (baseBytes[fullBytes] & (0xFF << (8 - remainingBits))) !=
                (clientBytes[fullBytes] & (0xFF << (8 - remainingBits))))
                return false;
            return true;
        }

        return false;
    }
}
