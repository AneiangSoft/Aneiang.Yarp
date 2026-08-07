using Aneiang.Yarp.Plugins;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

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
