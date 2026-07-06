using System.Collections.Concurrent;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Services;

/// <summary>
/// Manages per-rule-per-channel cooldown windows to prevent notification spam.
/// Extracted from <see cref="NotificationService"/> for single responsibility.
/// 
/// Memory optimization (v2.4): Periodic cleanup of expired cooldown entries
/// to prevent unbounded growth of the ConcurrentDictionary.
/// </summary>
internal static class CooldownManager
{
    private static readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();

    // Maximum cooldown duration across all rules (used as cleanup threshold).
    // Entries older than this are guaranteed expired and safe to remove.
    private static TimeSpan _maxCooldown = TimeSpan.FromMinutes(30);
    private static DateTime _lastCleanup = DateTime.UtcNow;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Sets the maximum cooldown duration (called when notification rules are loaded).
    /// </summary>
    public static void SetMaxCooldown(TimeSpan maxCooldown)
    {
        _maxCooldown = maxCooldown > TimeSpan.Zero ? maxCooldown : TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Computes the cooldown key for the given rule, channel, and event.
    /// </summary>
    public static string GetCooldownKey(string ruleId, string channelId, NotificationEvent evt)
    {
        var target = evt.ClusterId ?? evt.RouteId ?? evt.ClientIp ?? "global";
        return $"{ruleId}:{channelId}:{target}";
    }

    /// <summary>
    /// Returns true if the cooldown for the given key has expired (or doesn't exist),
    /// and updates the timestamp. Returns false if still in cooldown.
    /// Also performs periodic cleanup of expired entries.
    /// </summary>
    public static bool TryAcquire(string key, TimeSpan cooldown)
    {
        var now = DateTime.UtcNow;

        // Periodic cleanup: remove entries older than 2x max cooldown (safe margin)
        if (now - _lastCleanup > CleanupInterval)
        {
            _lastCleanup = now;
            var threshold = now - _maxCooldown * 2;
            foreach (var kvp in _cooldowns)
            {
                if (kvp.Value < threshold)
                    _cooldowns.TryRemove(kvp.Key, out _);
            }
        }

        if (_cooldowns.TryGetValue(key, out var lastAlert))
        {
            if (now - lastAlert < cooldown)
                return false;
        }
        _cooldowns[key] = now;
        return true;
    }
}
