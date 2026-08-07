/**
 * Traffic Metrics Config Module - Plugin binding manager (Route scope)
 */
(function() {
    'use strict';
    window.PluginBindingManager.create({
        pluginId: 'traffic-metrics',
        scope: 'Route',
        containerId: 'tm-config-content',
        refreshTimeId: 'tm-config-refresh-time',
        moduleName: 'TrafficMetricsConfigModule',
        icon: 'bi-graph-up-arrow',
        color: 'text-primary',
        titleKey: 'plugin.name.traffic-metrics',
        descKey: 'pluginPage.desc.traffic-metrics',
        helpKey: 'pluginPage.help.traffic-metrics'
    });
})();
