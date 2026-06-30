using System.Collections.Concurrent;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Services;

/// <summary>
/// Manages per-rule-per-channel cooldown windows to prevent notification spam.
/// Extracted from <see cref="NotificationService"/> for single responsibility.
/// </summary>
internal static class CooldownManager
{
    private static readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();

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
    /// </summary>
    public static bool TryAcquire(string key, TimeSpan cooldown)
    {
        var now = DateTime.UtcNow;
        if (_cooldowns.TryGetValue(key, out var lastAlert))
        {
            if (now - lastAlert < cooldown)
                return false;
        }
        _cooldowns[key] = now;
        return true;
    }
}
