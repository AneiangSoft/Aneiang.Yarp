using Aneiang.Yarp.Plugins;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Plugin;

/// <summary>
/// Invokes <see cref="IGatewayPlugin.ConfigureDashboard"/> once at startup for every
/// registered plugin and exposes the collected navigation items and widgets as a
/// stable, ordered snapshot for the Dashboard layout.
/// </summary>
public sealed class PluginDashboardInitializer
{
    public PluginDashboardInitializer(IEnumerable<IGatewayPlugin> plugins, IPluginDashboardBuilder builder)
    {
        foreach (var plugin in plugins)
        {
            plugin.ConfigureDashboard(builder);
        }

        NavItems = [.. builder.NavItems
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)];
        Widgets = [.. builder.Widgets
            .OrderBy(widget => widget.SortOrder)
            .ThenBy(widget => widget.Id, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Navigation items contributed by all plugins, ordered by <see cref="PluginNavItem.SortOrder"/>.</summary>
    public IReadOnlyList<PluginNavItem> NavItems { get; }

    /// <summary>Dashboard widgets contributed by all plugins, ordered by <see cref="PluginDashboardWidget.SortOrder"/>.</summary>
    public IReadOnlyList<PluginDashboardWidget> Widgets { get; }
}
