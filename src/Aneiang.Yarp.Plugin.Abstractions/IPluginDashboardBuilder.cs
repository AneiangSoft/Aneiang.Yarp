namespace Aneiang.Yarp.Plugins;

/// <summary>
/// Describes a navigation menu item contributed by a plugin.
/// </summary>
public sealed record PluginNavItem(
    string Id,
    string LabelKey,
    string Url,
    string? Icon = null,
    int SortOrder = 500);

/// <summary>
/// Describes a dashboard widget (card/panel) contributed by a plugin.
/// </summary>
public sealed record PluginDashboardWidget(
    string Id,
    string TitleKey,
    string PartialView,
    int SortOrder = 500);

/// <summary>
/// Builder interface that plugins use to contribute Dashboard navigation items
/// and widgets during <see cref="IGatewayPlugin.ConfigureDashboard"/>.
/// </summary>
public interface IPluginDashboardBuilder
{
    /// <summary>Add a navigation menu item.</summary>
    void AddNavItem(PluginNavItem item);

    /// <summary>Add a dashboard widget for the overview page.</summary>
    void AddWidget(PluginDashboardWidget widget);

    /// <summary>Gets all collected navigation items.</summary>
    IReadOnlyList<PluginNavItem> NavItems { get; }

    /// <summary>Gets all collected dashboard widgets.</summary>
    IReadOnlyList<PluginDashboardWidget> Widgets { get; }
}
