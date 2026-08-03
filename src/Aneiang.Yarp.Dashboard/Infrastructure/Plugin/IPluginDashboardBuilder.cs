namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

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

/// <summary>
/// Default implementation of <see cref="IPluginDashboardBuilder"/> that collects
/// contributions from all plugins into a single snapshot.
/// </summary>
public sealed class PluginDashboardBuilder : IPluginDashboardBuilder
{
    private readonly List<PluginNavItem> _navItems = [];
    private readonly List<PluginDashboardWidget> _widgets = [];

    /// <inheritdoc />
    public void AddNavItem(PluginNavItem item) => _navItems.Add(item);

    /// <inheritdoc />
    public void AddWidget(PluginDashboardWidget widget) => _widgets.Add(widget);

    /// <inheritdoc />
    public IReadOnlyList<PluginNavItem> NavItems => _navItems;

    /// <inheritdoc />
    public IReadOnlyList<PluginDashboardWidget> Widgets => _widgets;
}
