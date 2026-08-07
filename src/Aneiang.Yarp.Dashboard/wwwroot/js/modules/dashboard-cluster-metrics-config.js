/**
 * Cluster Metrics Config Module - Plugin binding manager (Cluster scope)
 */
(function() {
    'use strict';
    window.PluginBindingManager.create({
        pluginId: 'cluster-metrics',
        scope: 'Cluster',
        scopeLabel: __('pluginPage.cluster'),
        containerId: 'cm-config-content',
        refreshTimeId: 'cm-config-refresh-time',
        moduleName: 'ClusterMetricsConfigModule',
        icon: 'bi-diagram-3-fill',
        color: 'text-primary',
        titleKey: 'plugin.name.cluster-metrics',
        descKey: 'pluginPage.desc.cluster-metrics',
        helpKey: 'pluginPage.help.cluster-metrics'
    });
})();
