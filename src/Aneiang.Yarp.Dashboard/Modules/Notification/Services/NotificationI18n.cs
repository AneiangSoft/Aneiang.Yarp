using Aneiang.Yarp.Dashboard.Infrastructure.I18n;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Services;

/// <summary>
/// Server-side i18n helper for notification titles and messages.
/// Reads from the same <see cref="DashboardI18n"/> dictionaries used by the client-side UI.
/// </summary>
internal static class NotificationI18n
{
    /// <summary>
    /// Get the translated title for a notification event.
    /// </summary>
    public static string GetTitle(string eventType, string locale, params object?[] args)
    {
        var key = $"notif.msg.title.{eventType}";
        var template = Resolve(key, locale);
        if (string.IsNullOrEmpty(template))
        {
            // Fallback: use event type name directly
            var labelKey = $"notif.event.{eventType}";
            var label = Resolve(labelKey, locale);
            return !string.IsNullOrEmpty(label) ? label : eventType;
        }
        return args.Length > 0 ? string.Format(template, args) : template;
    }

    /// <summary>
    /// Get the translated body for a notification event.
    /// </summary>
    public static string GetBody(string eventType, string locale, params object?[] args)
    {
        var key = $"notif.msg.body.{eventType}";
        var template = Resolve(key, locale);
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }
        return args.Length > 0 ? string.Format(template, args) : template;
    }

    private static string? Resolve(string key, string? locale)
    {
        var dict = DashboardI18n.GetDict(locale);
        return dict.TryGetValue(key, out var value) ? value : null;
    }
}
