/**
 * Service Discovery Module - Full CRUD plugin binding manager (Cluster scope)
 */
(function() {
    'use strict';
    window.PluginBindingManager.create({
        pluginId: 'service-discovery',
        scope: 'Cluster',
        scopeLabel: __('pluginPage.cluster'),
        containerId: 'sd-content',
        refreshTimeId: 'sd-refresh-time',
        moduleName: 'ServiceDiscoveryModule',
        icon: 'bi-binoculars',
        color: 'text-primary',
        titleKey: 'plugin.name.service-discovery',
        descKey: 'pluginPage.desc.service-discovery',
        helpKey: 'pluginPage.help.service-discovery'
    });
})();
